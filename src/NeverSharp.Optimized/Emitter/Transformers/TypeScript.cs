// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/ts.ts (transformTypeScript)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/tstransforms/typeeraser.go and importelision.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; begun in module D1 to prove the transformation pipeline: type erasure
// of declarations, annotations and assertions, `declare` statements, interface and type alias removal, import and
// export elision through the emit resolver, classes without parameter properties, and const enum value substitution.
// Module D2 continues with enums, namespaces, parameter properties and import-equals of entity names (these paths
// throw NotSupportedException until then). TypeScript 6.0.3's default options only: no legacy decorators, no
// decorator metadata, no JSX transform, target ES2025.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>ts.ts transformTypeScript: the closure of the transformer is the instance.</summary>
internal sealed class TypeScriptTransformer
{
    // ts.ts TypeScriptSubstitutionFlags
    [Flags]
    private enum TypeScriptSubstitutionFlags
    {
        None = 0,
        /** Enables substitutions for namespace exports. */
        NamespaceExports = 1 << 1,
        /* Enables substitutions for unqualified enum members */
        NonQualifiedEnumMembers = 1 << 3,
    }

    // ts.ts ClassFacts
    [Flags]
    private enum ClassFacts
    {
        None = 0,
        HasStaticInitializedProperties = 1 << 0,
        HasClassOrConstructorParameterDecorators = 1 << 1,
        HasMemberDecorators = 1 << 2,
        IsExportOfNamespace = 1 << 3,
        IsNamedExternalExport = 1 << 4,
        IsDefaultExternalExport = 1 << 5,
        IsDerivedClass = 1 << 6,

        HasAnyDecorators = HasClassOrConstructorParameterDecorators | HasMemberDecorators,
        MayNeedImmediatelyInvokedFunctionExpression = HasAnyDecorators | HasStaticInitializedProperties,
        IsExported = IsExportOfNamespace | IsDefaultExternalExport | IsNamedExternalExport,
    }

    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly EmitResolver resolver;
    private readonly CompilerOptions compilerOptions;
    private readonly ScriptTarget languageVersion;
    private readonly ModuleKind moduleKind;
    private readonly bool legacyDecorators;

    // Save the previous transformation hooks.
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> previousOnEmitNode;
    private readonly Func<EmitHint, Node, Node> previousOnSubstituteNode;

    // These variables contain state that changes as we descend into the tree.
    private Node currentSourceFile;
    private Node currentNamespace;
    private Node currentLexicalScope;
    private Dictionary<string, Node>? currentScopeFirstDeclarationsOfName;

    // (enabledSubstitutions: whether expression substitution has been enabled for specific edge cases, which only
    // visitModuleDeclaration and visitEnumDeclaration enable: module D2.)

    // Keeps track of whether we are within any containing namespaces when performing
    // just-in-time substitution while printing an expression identifier.
    private TypeScriptSubstitutionFlags applicableSubstitutions;

    private readonly Func<Node, VisitResult> visitor;
    private readonly Func<Node, VisitResult> sourceElementVisitor;
    private readonly Func<Node, VisitResult> modifierVisitor;
    private readonly Func<Node, VisitResult> modifierElidingVisitor;
    private readonly Func<Node, VisitResult> decoratorElidingVisitor;

    private TypeScriptTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        resolver = context.GetEmitResolver();
        compilerOptions = context.GetCompilerOptions();
        languageVersion = compilerOptions.GetEmitScriptTarget();
        moduleKind = compilerOptions.GetEmitModuleKind();
        legacyDecorators = compilerOptions.ExperimentalDecorators == true;
        if (compilerOptions.EmitDecoratorMetadata == true)
        {
            throw new NotSupportedException("emitDecoratorMetadata (createRuntimeTypeSerializer) is not a Phase 4 option.");
        }

        // (currentNamespace: set by visitModuleDeclaration, module D2.)
        currentNamespace = Node.None;

        visitor = Visitor;
        sourceElementVisitor = SourceElementVisitor;
        modifierVisitor = ModifierVisitor;
        modifierElidingVisitor = ModifierElidingVisitor;
        decoratorElidingVisitor = DecoratorElidingVisitor;

        // Save the previous transformation hooks.
        previousOnEmitNode = context.OnEmitNode;
        previousOnSubstituteNode = context.OnSubstituteNode;

        // Set new transformation hooks.
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;

        // Enable substitution for property/element access to emit const enum values.
        context.EnableSubstitution(SyntaxKind.PropertyAccessExpression);
        context.EnableSubstitution(SyntaxKind.ElementAccessExpression);
    }

    private NodeStore store => context.Store;

    /// <summary>transformTypeScript(context).</summary>
    internal static Func<Node, Node> TransformTypeScript(TransformationContext context) => new TypeScriptTransformer(context).TransformSourceFile;

    /// <summary>Transform TypeScript-specific syntax in a SourceFile.</summary>
    private Node TransformSourceFile(Node node)
    {
        if (store.IsDeclarationFile(node))
        {
            return node;
        }

        currentSourceFile = node;

        var visited = SaveStateAndInvoke(node, VisitSourceFile);
        FactoryEmitNode.AddEmitHelpers(store, visited, context.ReadEmitHelpers());

        currentSourceFile = Node.None;
        return visited;
    }

    /// <summary>Visits a node, saving and restoring state variables on the stack.</summary>
    private T SaveStateAndInvoke<T>(Node node, Func<Node, T> f)
    {
        // Save state
        var savedCurrentScope = currentLexicalScope;
        var savedCurrentScopeFirstDeclarationsOfName = currentScopeFirstDeclarationsOfName;

        // Handle state changes before visiting a node.
        OnBeforeVisitNode(node);

        var visited = f(node);

        // Restore state
        if (currentLexicalScope != savedCurrentScope)
        {
            currentScopeFirstDeclarationsOfName = savedCurrentScopeFirstDeclarationsOfName;
        }

        currentLexicalScope = savedCurrentScope;
        return visited;
    }

    /// <summary>Performs actions that should always occur immediately before visiting a node.</summary>
    private void OnBeforeVisitNode(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.SourceFile:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.ModuleBlock:
            case SyntaxKind.Block:
                currentLexicalScope = node;
                currentScopeFirstDeclarationsOfName = null;
                break;

            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.FunctionDeclaration:
                if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Ambient))
                {
                    break;
                }

                // Record these declarations provided that they have a name.
                if (!store.Name(node).IsNone)
                {
                    RecordEmittedDeclarationInScope(node);
                }
                else
                {
                    // These nodes should always have names unless they are default-exports;
                    // however, class declaration parsing allows for undefined names, so syntactically invalid
                    // programs may also have an undefined name.
                    Debug.Assert(store.Kind(node) == SyntaxKind.ClassDeclaration || Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default));
                }

                break;
        }
    }

    /// <summary>General-purpose node visitor.</summary>
    private VisitResult Visitor(Node node) => SaveStateAndInvoke(node, VisitorWorker);

    /// <summary>Visits and possibly transforms any node.</summary>
    private VisitResult VisitorWorker(Node node)
    {
        if ((store.TransformFlags(node) & TransformFlags.ContainsTypeScript) != 0)
        {
            return VisitTypeScript(node);
        }
        return node;
    }

    /// <summary>Specialized visitor that visits the immediate children of a SourceFile.</summary>
    private VisitResult SourceElementVisitor(Node node) => SaveStateAndInvoke(node, SourceElementVisitorWorker);

    private VisitResult SourceElementVisitorWorker(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
                return VisitElidableStatement(node);
            default:
                return VisitorWorker(node);
        }
    }

    /// <summary>
    /// Determines whether import/export elision is blocked for this statement: when the statement was modified by a
    /// `before` custom transform (Track O has none, so a statement is always its parse tree node).
    /// </summary>
    private bool IsElisionBlocked(Node node)
    {
        var parsed = UtilitiesPublic.GetParseTreeNode(store, node);
        if (parsed == node || store.Kind(node) == SyntaxKind.ExportAssignment)
        {
            return false;
        }

        if (parsed.IsNone || store.Kind(parsed) != store.Kind(node))
        {
            // no longer safe to elide as the declaration was replaced with a node of a different kind
            return true;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
                if (store.ImportClause(node) != store.ImportClause(parsed))
                {
                    return true; // no longer safe to elide as the import clause has changed
                }
                if (store.Attributes(node) != store.Attributes(parsed))
                {
                    return true; // no longer safe to elide as the import attributes have changed
                }
                break;
            case SyntaxKind.ImportEqualsDeclaration:
                if (store.Name(node) != store.Name(parsed))
                {
                    return true; // no longer safe to elide as local binding has changed
                }
                if (store.IsTypeOnly(node) != store.IsTypeOnly(parsed))
                {
                    return true; // no longer safe to elide as `type` modifier has changed
                }
                if (store.ModuleReference(node) != store.ModuleReference(parsed) && (IsEntityName(store.ModuleReference(node)) || IsEntityName(store.ModuleReference(parsed))))
                {
                    return true; // no longer safe to elide as EntityName reference has changed.
                }
                break;
            case SyntaxKind.ExportDeclaration:
                if (store.ExportClause(node) != store.ExportClause(parsed))
                {
                    return true; // no longer safe to elide as the export clause has changed
                }
                if (store.Attributes(node) != store.Attributes(parsed))
                {
                    return true; // no longer safe to elide as the export attributes have changed
                }
                break;
        }

        return false;
    }

    private bool IsEntityName(Node node) => store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.QualifiedName;

    private VisitResult VisitElidableStatement(Node node)
    {
        if (IsElisionBlocked(node))
        {
            // We do not reuse `visitorWorker`, as the ellidable statement syntax kinds are technically unrecognized by
            // the switch-case in `visitTypeScript`, and will trigger debug failures when debug verbosity is turned up.
            if ((store.TransformFlags(node) & TransformFlags.ContainsTypeScript) != 0)
            {
                // This node contains TypeScript, so we should visit its children.
                return Visitors.VisitEachChild(node, visitor, context);
            }
            // Otherwise, we can just return the node
            return node;
        }
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
                return VisitImportDeclaration(node);
            case SyntaxKind.ImportEqualsDeclaration:
                return VisitImportEqualsDeclaration(node);
            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment(node);
            case SyntaxKind.ExportDeclaration:
                return VisitExportDeclaration(node);
            default:
                Debug.Fail("Unhandled ellided statement");
                return node;
        }
    }

    /// <summary>Gets a specialized visitor that visits the immediate children of a class with TypeScript syntax.</summary>
    private Func<Node, VisitResult> GetClassElementVisitor(Node parent)
    {
        return node => SaveStateAndInvoke(node, n => ClassElementVisitorWorker(n, parent));
    }

    private VisitResult ClassElementVisitorWorker(Node node, Node parent)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Constructor:
                return VisitConstructor(node);

            case SyntaxKind.PropertyDeclaration:
                // Property declarations are not TypeScript syntax, but they must be visited
                // for the decorator transformation.
                return VisitPropertyDeclaration(node, parent);

            case SyntaxKind.GetAccessor:
                // Get Accessors can have TypeScript modifiers, decorators, and type annotations.
                return VisitGetAccessor(node, parent);

            case SyntaxKind.SetAccessor:
                // Set Accessors can have TypeScript modifiers and type annotations.
                return VisitSetAccessor(node, parent);

            case SyntaxKind.MethodDeclaration:
                // TypeScript method declarations may have decorators, modifiers
                // or type annotations.
                return VisitMethodDeclaration(node, parent);

            case SyntaxKind.ClassStaticBlockDeclaration:
                return Visitors.VisitEachChild(node, visitor, context);

            case SyntaxKind.SemicolonClassElement:
                return node;

            case SyntaxKind.IndexSignature:
                // Index signatures are elided
                return VisitResult.None;

            default:
                throw Debug.UnexpectedKind(store.Kind(node), "a class element");
        }
    }

    private Func<Node, VisitResult> GetObjectLiteralElementVisitor(Node parent)
    {
        return node => SaveStateAndInvoke(node, n => ObjectLiteralElementVisitorWorker(n, parent));
    }

    private VisitResult ObjectLiteralElementVisitorWorker(Node node, Node parent)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.SpreadAssignment:
                return Visitor(node);

            case SyntaxKind.GetAccessor:
                // Get Accessors can have TypeScript modifiers, decorators, and type annotations.
                return VisitGetAccessor(node, parent);

            case SyntaxKind.SetAccessor:
                // Set Accessors can have TypeScript modifiers and type annotations.
                return VisitSetAccessor(node, parent);

            case SyntaxKind.MethodDeclaration:
                // TypeScript method declarations may have decorators, modifiers
                // or type annotations.
                return VisitMethodDeclaration(node, parent);

            default:
                throw Debug.UnexpectedKind(store.Kind(node), "an object literal element");
        }
    }

    private VisitResult DecoratorElidingVisitor(Node node) => store.Kind(node) == SyntaxKind.Decorator ? VisitResult.None : Visitor(node);

    private VisitResult ModifierElidingVisitor(Node node) => UtilitiesPublic.IsModifier(store, node) ? VisitResult.None : Visitor(node);

    private VisitResult ModifierVisitor(Node node)
    {
        if (store.Kind(node) == SyntaxKind.Decorator)
        {
            return VisitResult.None;
        }
        if ((Utilities.ModifierToFlag(store.Kind(node)) & ModifierFlags.TypeScriptModifier) != 0)
        {
            return VisitResult.None;
        }
        else if (!currentNamespace.IsNone && store.Kind(node) == SyntaxKind.ExportKeyword)
        {
            return VisitResult.None;
        }

        return node;
    }

    /// <summary>Branching visitor, visits a TypeScript syntax node.</summary>
    private VisitResult VisitTypeScript(Node node)
    {
        if (TransformerUtilities.IsStatement(store, node) && Utilities.HasSyntacticModifier(store, node, ModifierFlags.Ambient))
        {
            // TypeScript ambient declarations are elided, but some comments may be preserved.
            // See the implementation of `getLeadingComments` in comments.ts for more details.
            return factory.CreateNotEmittedStatement(node);
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.DefaultKeyword:
                // ES6 export and default modifiers are elided when inside a namespace.
                return !currentNamespace.IsNone ? VisitResult.None : node;

            case SyntaxKind.PublicKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.OverrideKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.InKeyword:
            case SyntaxKind.OutKeyword:
            // TypeScript accessibility and readonly modifiers are elided
            // falls through
            case SyntaxKind.ArrayType:
            case SyntaxKind.TupleType:
            case SyntaxKind.OptionalType:
            case SyntaxKind.RestType:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.TypePredicate:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.TypeQuery:
            case SyntaxKind.TypeReference:
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            case SyntaxKind.ConditionalType:
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.ThisType:
            case SyntaxKind.TypeOperator:
            case SyntaxKind.IndexedAccessType:
            case SyntaxKind.MappedType:
            case SyntaxKind.LiteralType:
            // TypeScript type nodes are elided.
            // falls through
            case SyntaxKind.IndexSignature:
                // TypeScript index signatures are elided.
                return VisitResult.None;

            case SyntaxKind.TypeAliasDeclaration:
                // TypeScript type-only declarations are elided.
                return factory.CreateNotEmittedStatement(node);

            case SyntaxKind.NamespaceExportDeclaration:
                // TypeScript namespace export declarations are elided.
                return VisitResult.None;

            case SyntaxKind.InterfaceDeclaration:
                // TypeScript interfaces are elided, but some comments may be preserved.
                // See the implementation of `getLeadingComments` in comments.ts for more details.
                return factory.CreateNotEmittedStatement(node);

            case SyntaxKind.ClassDeclaration:
                // This may be a class declaration with TypeScript syntax extensions.
                return VisitClassDeclaration(node);

            case SyntaxKind.ClassExpression:
                // This may be a class expression with TypeScript syntax extensions.
                return VisitClassExpression(node);

            case SyntaxKind.HeritageClause:
                // This may be a heritage clause with TypeScript syntax extensions.
                return VisitHeritageClause(node);

            case SyntaxKind.ExpressionWithTypeArguments:
                // TypeScript supports type arguments on an expression in an `extends` heritage clause.
                return VisitExpressionWithTypeArguments(node);

            case SyntaxKind.ObjectLiteralExpression:
                return VisitObjectLiteralExpression(node);

            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ClassStaticBlockDeclaration:
                Debug.Fail("Class and object literal elements must be visited with their respective visitors");
                return node;

            case SyntaxKind.FunctionDeclaration:
                // Typescript function declarations can have modifiers, decorators, and type annotations.
                return VisitFunctionDeclaration(node);

            case SyntaxKind.FunctionExpression:
                // TypeScript function expressions can have modifiers and type annotations.
                return VisitFunctionExpression(node);

            case SyntaxKind.ArrowFunction:
                // TypeScript arrow functions can have modifiers and type annotations.
                return VisitArrowFunction(node);

            case SyntaxKind.Parameter:
                // This may be a parameter declaration with TypeScript syntax extensions.
                return VisitParameter(node);

            case SyntaxKind.ParenthesizedExpression:
                // ParenthesizedExpressions are TypeScript if their expression is a
                // TypeAssertion or AsExpression
                return VisitParenthesizedExpression(node);

            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                // TypeScript type assertions are removed, but their subtrees are preserved.
                return VisitAssertionExpression(node);

            case SyntaxKind.SatisfiesExpression:
                return VisitSatisfiesExpression(node);

            case SyntaxKind.CallExpression:
                return VisitCallExpression(node);

            case SyntaxKind.NewExpression:
                return VisitNewExpression(node);

            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression(node);

            case SyntaxKind.NonNullExpression:
                // TypeScript non-null expressions are removed, but their subtrees are preserved.
                return VisitNonNullExpression(node);

            case SyntaxKind.EnumDeclaration:
                // TypeScript enum declarations do not exist in ES6 and must be rewritten.
                // (visitEnumDeclaration is module D2's; until then the declaration is left as it is, so the unit's
                // .js output differs, while its other outputs stay comparable.)
                return Visitors.VisitEachChild(node, visitor, context);

            case SyntaxKind.VariableStatement:
                // TypeScript namespace exports for variable statements must be transformed.
                return VisitVariableStatement(node);

            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration(node);

            case SyntaxKind.ModuleDeclaration:
                // TypeScript namespace declarations must be transformed.
                // (visitModuleDeclaration is module D2's; until then the declaration is left as it is, see above.)
                return Visitors.VisitEachChild(node, visitor, context);

            case SyntaxKind.ImportEqualsDeclaration:
                // TypeScript namespace or external module import.
                return VisitImportEqualsDeclaration(node);

            case SyntaxKind.JsxSelfClosingElement:
                return VisitJsxSelfClosingElement(node);

            case SyntaxKind.JsxOpeningElement:
                return VisitJsxJsxOpeningElement(node);

            default:
                // node contains some other TypeScript syntax
                return Visitors.VisitEachChild(node, visitor, context);
        }
    }

    private Node VisitSourceFile(Node node)
    {
        var alwaysStrict = compilerOptions.GetAlwaysStrict() &&
            !(Utilities.IsExternalModule(store, node) && moduleKind >= ModuleKind.ES2015) &&
            store.ScriptKind(node) != ScriptKind.JSON;

        return factory.UpdateSourceFile(
            node,
            Visitors.VisitLexicalEnvironment(store.Statements(node), sourceElementVisitor, context, start: 0, alwaysStrict));
    }

    private Node VisitObjectLiteralExpression(Node node)
    {
        return factory.UpdateObjectLiteralExpression(
            node,
            Visitors.VisitNodes(factory, store.Properties(node), GetObjectLiteralElementVisitor(node)));
    }

    private ClassFacts GetClassFacts(Node node)
    {
        var facts = ClassFacts.None;
        if (TransformerUtilities.HasProperties(store, node, requireInitializer: true, isStatic: true))
        {
            facts |= ClassFacts.HasStaticInitializedProperties;
        }
        var extendsClauseElement = TransformerUtilities.GetEffectiveBaseTypeNode(store, node);
        if (!extendsClauseElement.IsNone && store.Kind(FactoryUtilities.SkipOuterExpressions(store, store.Expression(extendsClauseElement))) != SyntaxKind.NullKeyword)
        {
            facts |= ClassFacts.IsDerivedClass;
        }
        if (TransformerUtilities.ClassOrConstructorParameterIsDecorated(store, legacyDecorators, node))
        {
            facts |= ClassFacts.HasClassOrConstructorParameterDecorators;
        }
        if (TransformerUtilities.ChildIsDecorated(store, legacyDecorators, node))
        {
            facts |= ClassFacts.HasMemberDecorators;
        }
        if (IsExportOfNamespace(node))
        {
            facts |= ClassFacts.IsExportOfNamespace;
        }
        else if (IsDefaultExternalModuleExport(node))
        {
            facts |= ClassFacts.IsDefaultExternalExport;
        }
        else if (IsNamedExternalModuleExport(node))
        {
            facts |= ClassFacts.IsNamedExternalExport;
        }
        return facts;
    }

    private bool HasTypeScriptClassSyntax(Node node) => (store.TransformFlags(node) & TransformFlags.ContainsTypeScriptClassSyntax) != 0;

    private bool IsClassLikeDeclarationWithTypeScriptSyntax(Node node)
    {
        if (TransformerUtilities.HasDecorators(store, node) || store.Length(store.TypeParameters(node)) > 0)
        {
            return true;
        }
        foreach (var clause in store.Nodes(store.HeritageClauses(node)))
        {
            if (HasTypeScriptClassSyntax(clause))
            {
                return true;
            }
        }
        foreach (var member in store.Nodes(store.Members(node)))
        {
            if (HasTypeScriptClassSyntax(member))
            {
                return true;
            }
        }
        return false;
    }

    private VisitResult VisitClassDeclaration(Node node)
    {
        var facts = GetClassFacts(node);
        var promoteToIIFE = languageVersion < ScriptTarget.ES2015 &&
            (facts & ClassFacts.MayNeedImmediatelyInvokedFunctionExpression) != 0;
        Debug.Assert(!promoteToIIFE, "Targets below ES2015 are not Phase 4 options.");

        if (!IsClassLikeDeclarationWithTypeScriptSyntax(node) &&
            !TransformerUtilities.ClassOrConstructorParameterIsDecorated(store, legacyDecorators, node) &&
            !IsExportOfNamespace(node))
        {
            return factory.UpdateClassDeclaration(
                node,
                Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor),
                store.Name(node),
                default,
                Visitors.VisitNodes(factory, store.HeritageClauses(node), visitor),
                Visitors.VisitNodes(factory, store.Members(node), GetClassElementVisitor(node)));
        }

        var moveModifiers = promoteToIIFE ||
            (facts & ClassFacts.IsExportOfNamespace) != 0;

        // elide modifiers on the declaration if we are emitting an IIFE or the class is
        // a namespace export
        var modifiers = moveModifiers ?
            Visitors.VisitNodes(factory, store.Modifiers(node), modifierElidingVisitor) :
            Visitors.VisitNodes(factory, store.Modifiers(node), visitor);

        // (inject metadata only if the class is decorated: injectClassTypeMetadata adds nothing without legacy
        // decorators, getTypeMetadata.)

        var needsName = (moveModifiers && store.Name(node).IsNone) ||
            (facts & ClassFacts.HasMemberDecorators) != 0 ||
            (facts & ClassFacts.HasStaticInitializedProperties) != 0;

        var name = needsName ?
            (store.Name(node).IsNone ? factory.GetGeneratedNameForNode(node) : store.Name(node)) :
            store.Name(node);

        //  ${modifiers} class ${name} ${heritageClauses} {
        //      ${members}
        //  }
        var classDeclaration = factory.UpdateClassDeclaration(
            node,
            modifiers,
            name,
            default,
            Visitors.VisitNodes(factory, store.HeritageClauses(node), visitor),
            TransformClassMembers(node));

        // To better align with the old emitter, we should not emit a trailing source map
        // entry if the class has static properties.
        var emitFlags = Utilities.GetEmitFlags(store, node);
        if ((facts & ClassFacts.HasStaticInitializedProperties) != 0)
        {
            emitFlags |= EmitFlags.NoTrailingSourceMap;
        }

        FactoryEmitNode.SetEmitFlags(store, classDeclaration, emitFlags);

        var statement = classDeclaration;

        if (moveModifiers)
        {
            if ((facts & ClassFacts.IsExportOfNamespace) != 0)
            {
                throw new NotSupportedException("createExportMemberAssignmentStatement is not ported yet (module D2).");
            }
            if ((facts & ClassFacts.IsDefaultExternalExport) != 0)
            {
                return VisitResult.Many([
                    statement,
                    factory.CreateExportDefault(factory.GetLocalName(node, allowComments: false, allowSourceMaps: true)),
                ]);
            }
            if ((facts & ClassFacts.IsNamedExternalExport) != 0)
            {
                return VisitResult.Many([
                    statement,
                    factory.CreateExternalModuleExport(factory.GetDeclarationName(node, allowComments: false, allowSourceMaps: true)),
                ]);
            }
        }

        return statement;
    }

    private Node VisitClassExpression(Node node)
    {
        var modifiers = Visitors.VisitNodes(factory, store.Modifiers(node), modifierElidingVisitor);
        // (injectClassTypeMetadata adds nothing without legacy decorators.)

        return factory.UpdateClassExpression(
            node,
            modifiers,
            store.Name(node),
            default,
            Visitors.VisitNodes(factory, store.HeritageClauses(node), visitor),
            TransformClassMembers(node));
    }

    /// <summary>Transforms the members of a class.</summary>
    private NodeList TransformClassMembers(Node node)
    {
        var members = Visitors.VisitNodes(factory, store.Members(node), GetClassElementVisitor(node));

        List<Node>? newMembers = null;
        var constructor = TransformerUtilities.GetFirstConstructorWithBody(store, node);
        if (!constructor.IsNone)
        {
            foreach (var parameter in store.Nodes(store.Parameters(constructor)))
            {
                if (!Utilities.IsParameterPropertyDeclaration(store, parameter, constructor))
                {
                    continue;
                }
                // Ignore parameter properties with destructured names; we will have errored on them earlier.
                if (store.Kind(store.Name(parameter)) != SyntaxKind.Identifier)
                {
                    continue;
                }
                var parameterProperty = factory.CreatePropertyDeclaration(
                    default,
                    store.Name(parameter),
                    default,
                    default,
                    default);
                NodeFactory.SetOriginalNode(store, parameterProperty, parameter);
                (newMembers ??= []).Add(parameterProperty);
            }
        }

        if (newMembers is not null)
        {
            newMembers.AddRange(store.Nodes(members));
            return FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(newMembers), store.Members(node));
        }

        return members;
    }

    /// <summary>Visits the property name of a class element, for use when emitting property initializers.</summary>
    private Node VisitPropertyNameOfClassElement(Node member)
    {
        var name = store.Name(member);
        // (Computed property names of decorated members are hoisted with legacy decorators only.)
        return Visitors.VisitNode(factory, name, visitor);
    }

    /// <summary>Transforms a HeritageClause with TypeScript syntax: `implements` clauses are elided.</summary>
    private VisitResult VisitHeritageClause(Node node)
    {
        if (store.Token(node) == SyntaxKind.ImplementsKeyword)
        {
            // implements clauses are elided
            return VisitResult.None;
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    /// <summary>Transforms an ExpressionWithTypeArguments with TypeScript syntax (its type arguments are elided).</summary>
    private Node VisitExpressionWithTypeArguments(Node node)
    {
        return factory.UpdateExpressionWithTypeArguments(
            node,
            Visitors.VisitNode(factory, store.Expression(node), visitor),
            default);
    }

    /// <summary>Whether to emit a function-like declaration: not if it does not have a body.</summary>
    private bool ShouldEmitFunctionLikeDeclaration(Node node) => !Utilities.NodeIsMissing(store, store.Body(node));

    private VisitResult VisitPropertyDeclaration(Node node, Node parent)
    {
        var isAmbient = (store.Flags(node) & NodeFlags.Ambient) != 0 || Utilities.HasSyntacticModifier(store, node, ModifierFlags.Abstract);
        if (isAmbient && !(legacyDecorators && TransformerUtilities.HasDecorators(store, node)))
        {
            return VisitResult.None;
        }

        var modifiers = Utilities.IsClassLike(store, parent) ? !isAmbient ?
            Visitors.VisitNodes(factory, store.Modifiers(node), visitor) :
            Visitors.VisitNodes(factory, store.Modifiers(node), modifierElidingVisitor) :
            Visitors.VisitNodes(factory, store.Modifiers(node), decoratorElidingVisitor);

        // (injectClassElementTypeMetadata adds nothing without legacy decorators.)

        // Preserve a `declare x` property with decorators to be handled by the decorators transform
        if (isAmbient)
        {
            throw new NotSupportedException("Ambient properties with legacy decorators are not Phase 4 options.");
        }

        return factory.UpdatePropertyDeclaration(
            node,
            modifiers,
            VisitPropertyNameOfClassElement(node),
            default,
            default,
            Visitors.VisitNode(factory, store.Initializer(node), visitor));
    }

    private VisitResult VisitConstructor(Node node)
    {
        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return VisitResult.None;
        }

        return factory.UpdateConstructorDeclaration(
            node,
            default,
            Visitors.VisitParameterList(store.Parameters(node), visitor, context),
            TransformConstructorBody(store.Body(node), node));
    }

    private void TransformConstructorBodyWorker(List<Node> statementsOut, NodeList statementsIn, int statementOffset, List<int> superPath, int superPathDepth, List<Node> initializerStatements)
    {
        var superStatementIndex = superPath[superPathDepth];
        var superStatement = store.Element(statementsIn, superStatementIndex);
        statementsOut.AddRange(store.Nodes(Visitors.VisitNodes(factory, statementsIn, visitor, statementOffset, superStatementIndex - statementOffset)));
        if (store.Kind(superStatement) == SyntaxKind.TryStatement)
        {
            var tryBlockStatements = new List<Node>();

            TransformConstructorBodyWorker(
                tryBlockStatements,
                store.Statements(store.TryBlock(superStatement)),
                statementOffset: 0,
                superPath,
                superPathDepth + 1,
                initializerStatements);

            var tryBlockStatementsArray = factory.CreateNodeArray(tryBlockStatements);
            FactoryUtilitiesPublic.SetTextRange(store, tryBlockStatementsArray, store.Statements(store.TryBlock(superStatement)));

            statementsOut.Add(factory.UpdateTryStatement(
                superStatement,
                factory.UpdateBlock(store.TryBlock(superStatement), factory.CreateNodeArray(tryBlockStatements)),
                Visitors.VisitNode(factory, store.CatchClause(superStatement), visitor),
                Visitors.VisitNode(factory, store.FinallyBlock(superStatement), visitor)));
        }
        else
        {
            statementsOut.AddRange(store.Nodes(Visitors.VisitNodes(factory, statementsIn, visitor, superStatementIndex, 1)));
            statementsOut.AddRange(initializerStatements);
        }
        statementsOut.AddRange(store.Nodes(Visitors.VisitNodes(factory, statementsIn, visitor, superStatementIndex + 1)));
    }

    private Node TransformConstructorBody(Node body, Node constructor)
    {
        var parametersWithPropertyAssignments = new List<Node>();
        foreach (var parameter in store.Nodes(store.Parameters(constructor)))
        {
            if (Utilities.IsParameterPropertyDeclaration(store, parameter, constructor))
            {
                parametersWithPropertyAssignments.Add(parameter);
            }
        }
        if (parametersWithPropertyAssignments.Count == 0)
        {
            return Visitors.VisitFunctionBody(body, visitor, context);
        }

        var statements = new List<Node>();

        context.ResumeLexicalEnvironment();

        var prologueStatementCount = factory.CopyPrologue(store.Nodes(store.Statements(body)), statements, ensureUseStrict: false, visitor);
        var superPath = TransformerUtilities.FindSuperStatementIndexPath(store, store.Statements(body), prologueStatementCount);

        // Transform parameters into property assignments. Transforms this:
        //
        //  constructor (public x, public y) {
        //  }
        //
        // Into this:
        //
        //  constructor (x, y) {
        //      this.x = x;
        //      this.y = y;
        //  }
        //
        var parameterPropertyAssignments = new List<Node>();
        foreach (var parameter in parametersWithPropertyAssignments)
        {
            var assignment = TransformParameterWithPropertyAssignment(parameter);
            if (!assignment.IsNone)
            {
                parameterPropertyAssignments.Add(assignment);
            }
        }
        if (superPath.Count > 0)
        {
            TransformConstructorBodyWorker(statements, store.Statements(body), prologueStatementCount, superPath, superPathDepth: 0, parameterPropertyAssignments);
        }
        else
        {
            statements.AddRange(parameterPropertyAssignments);
            statements.AddRange(store.Nodes(Visitors.VisitNodes(factory, store.Statements(body), visitor, prologueStatementCount)));
        }

        // End the lexical environment.
        statements = factory.MergeLexicalEnvironment(statements, context.EndLexicalEnvironment());
        var block = factory.CreateBlock(FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(statements), store.Statements(body)), multiLine: true);
        FactoryUtilitiesPublic.SetTextRange(store, block, body);
        NodeFactory.SetOriginalNode(store, block, body);
        return block;
    }

    /// <summary>Transforms a parameter into a property assignment statement.</summary>
    private Node TransformParameterWithPropertyAssignment(Node node)
    {
        var name = store.Name(node);
        if (store.Kind(name) != SyntaxKind.Identifier)
        {
            return Node.None;
        }

        // TODO(rbuckton): Does this need to be parented?
        var propertyName = FactoryUtilitiesPublic.SetTextRange(store, factory.CloneNode(name), name);
        store.SetParent(propertyName, store.Parent(name));
        FactoryEmitNode.SetEmitFlags(store, propertyName, EmitFlags.NoComments | EmitFlags.NoSourceMap);

        // TODO(rbuckton): Does this need to be parented?
        var localName = FactoryUtilitiesPublic.SetTextRange(store, factory.CloneNode(name), name);
        store.SetParent(localName, store.Parent(name));
        FactoryEmitNode.SetEmitFlags(store, localName, EmitFlags.NoComments);

        var access = FactoryUtilitiesPublic.SetTextRange(store, factory.CreatePropertyAccessExpression(factory.CreateToken(SyntaxKind.ThisKeyword), propertyName), store.Name(node));
        var statement = factory.CreateExpressionStatement(factory.CreateAssignment(access, localName));
        NodeFactory.SetOriginalNode(store, statement, node);
        // moveRangePos(node, -1)
        Utilities.SetTextRangePosEnd(store, statement, -1, store.End(node));
        FactoryEmitNode.RemoveAllComments(store, statement);
        return FactoryEmitNode.SetStartsOnNewLine(store, statement, newLine: true);
    }

    private VisitResult VisitMethodDeclaration(Node node, Node parent)
    {
        if ((store.TransformFlags(node) & TransformFlags.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return VisitResult.None;
        }

        var modifiers = Utilities.IsClassLike(store, parent) ?
            Visitors.VisitNodes(factory, store.Modifiers(node), visitor) :
            Visitors.VisitNodes(factory, store.Modifiers(node), decoratorElidingVisitor);

        // (injectClassElementTypeMetadata adds nothing without legacy decorators.)

        var name = VisitPropertyNameOfClassElement(node);
        var parameters = Visitors.VisitParameterList(store.Parameters(node), visitor, context);
        return factory.UpdateMethodDeclaration(
            node,
            modifiers,
            store.AsteriskToken(node),
            name,
            default,
            default,
            parameters,
            default,
            Visitors.VisitFunctionBody(store.Body(node), visitor, context));
    }

    /// <summary>Whether to emit an accessor declaration: not if it does not have a body and is abstract.</summary>
    private bool ShouldEmitAccessorDeclaration(Node node)
    {
        return !(Utilities.NodeIsMissing(store, store.Body(node)) && Utilities.HasSyntacticModifier(store, node, ModifierFlags.Abstract));
    }

    private VisitResult VisitGetAccessor(Node node, Node parent)
    {
        if ((store.TransformFlags(node) & TransformFlags.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (!ShouldEmitAccessorDeclaration(node))
        {
            return VisitResult.None;
        }

        var modifiers = Utilities.IsClassLike(store, parent) ?
            Visitors.VisitNodes(factory, store.Modifiers(node), visitor) :
            Visitors.VisitNodes(factory, store.Modifiers(node), decoratorElidingVisitor);

        var name = VisitPropertyNameOfClassElement(node);
        var parameters = Visitors.VisitParameterList(store.Parameters(node), visitor, context);
        var body = Visitors.VisitFunctionBody(store.Body(node), visitor, context);
        return factory.UpdateGetAccessorDeclaration(
            node,
            modifiers,
            name,
            parameters,
            default,
            body.IsNone ? factory.CreateBlock(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)) : body);
    }

    private VisitResult VisitSetAccessor(Node node, Node parent)
    {
        if ((store.TransformFlags(node) & TransformFlags.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (!ShouldEmitAccessorDeclaration(node))
        {
            return VisitResult.None;
        }

        var modifiers = Utilities.IsClassLike(store, parent) ?
            Visitors.VisitNodes(factory, store.Modifiers(node), visitor) :
            Visitors.VisitNodes(factory, store.Modifiers(node), decoratorElidingVisitor);

        var name = VisitPropertyNameOfClassElement(node);
        var parameters = Visitors.VisitParameterList(store.Parameters(node), visitor, context);
        var body = Visitors.VisitFunctionBody(store.Body(node), visitor, context);
        return factory.UpdateSetAccessorDeclaration(
            node,
            modifiers,
            name,
            parameters,
            body.IsNone ? factory.CreateBlock(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)) : body);
    }

    private VisitResult VisitFunctionDeclaration(Node node)
    {
        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return factory.CreateNotEmittedStatement(node);
        }
        var modifiers = Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor);
        var parameters = Visitors.VisitParameterList(store.Parameters(node), visitor, context);
        var body = Visitors.VisitFunctionBody(store.Body(node), visitor, context);
        var updated = factory.UpdateFunctionDeclaration(
            node,
            modifiers,
            store.AsteriskToken(node),
            store.Name(node),
            default,
            parameters,
            default,
            body.IsNone ? factory.CreateBlock(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)) : body);
        if (IsExportOfNamespace(node))
        {
            throw new NotSupportedException("addExportMemberAssignment is not ported yet (module D2).");
        }
        return updated;
    }

    private Node VisitFunctionExpression(Node node)
    {
        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return factory.CreateOmittedExpression();
        }
        var modifiers = Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor);
        var parameters = Visitors.VisitParameterList(store.Parameters(node), visitor, context);
        var body = Visitors.VisitFunctionBody(store.Body(node), visitor, context);
        return factory.UpdateFunctionExpression(
            node,
            modifiers,
            store.AsteriskToken(node),
            store.Name(node),
            default,
            parameters,
            default,
            body.IsNone ? factory.CreateBlock(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)) : body);
    }

    private Node VisitArrowFunction(Node node)
    {
        var modifiers = Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor);
        var parameters = Visitors.VisitParameterList(store.Parameters(node), visitor, context);
        return factory.UpdateArrowFunction(
            node,
            modifiers,
            default,
            parameters,
            default,
            store.EqualsGreaterThanToken(node),
            Visitors.VisitFunctionBody(store.Body(node), visitor, context));
    }

    private VisitResult VisitParameter(Node node)
    {
        if (TransformerUtilities.ParameterIsThisKeyword(store, node))
        {
            return VisitResult.None;
        }

        var updated = factory.UpdateParameterDeclaration(
            node,
            Visitors.VisitNodes(factory, store.Modifiers(node), n => store.Kind(n) == SyntaxKind.Decorator ? Visitor(n) : VisitResult.None),
            store.DotDotDotToken(node),
            Visitors.VisitNode(factory, store.Name(node), visitor),
            default,
            default,
            Visitors.VisitNode(factory, store.Initializer(node), visitor));
        if (updated != node)
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            FactoryEmitNode.SetCommentRange(store, updated, node);
            var moved = MoveRangePastModifiers(node);
            Utilities.SetTextRangePosEnd(store, updated, moved.Pos, moved.End);
            FactoryEmitNode.SetSourceMapRange(store, updated, moved);
            FactoryEmitNode.SetEmitFlags(store, store.Name(updated), EmitFlags.NoTrailingSourceMap);
        }
        return updated;
    }

    // utilities.ts moveRangePastModifiers: past the last modifier, or past the decorators, or the node's range.
    private ObjectTextRange MoveRangePastModifiers(Node node)
    {
        var modifiers = store.Nodes(store.Modifiers(node));
        if (store.Kind(node) is SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration)
        {
            // moveRangePos(node, node.name.pos)
            return new ObjectTextRange { Pos = store.Pos(store.Name(node)), End = store.End(node) };
        }
        var lastModifier = modifiers.Length > 0 ? modifiers[^1] : Node.None;
        if (!lastModifier.IsNone && !Utilities.PositionIsSynthesized(store.End(lastModifier)))
        {
            return new ObjectTextRange { Pos = store.End(lastModifier), End = store.End(node) };
        }
        // moveRangePastDecorators
        var lastDecorator = Node.None;
        for (var i = modifiers.Length - 1; i >= 0; i--)
        {
            if (store.Kind(modifiers[i]) == SyntaxKind.Decorator)
            {
                lastDecorator = modifiers[i];
                break;
            }
        }
        return !lastDecorator.IsNone && !Utilities.PositionIsSynthesized(store.End(lastDecorator))
            ? new ObjectTextRange { Pos = store.End(lastDecorator), End = store.End(node) }
            : new ObjectTextRange { Pos = store.Pos(node), End = store.End(node) };
    }

    private VisitResult VisitVariableStatement(Node node)
    {
        if (IsExportOfNamespace(node))
        {
            throw new NotSupportedException("Namespace exports of variables are not ported yet (module D2).");
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitVariableDeclaration(Node node)
    {
        var updated = factory.UpdateVariableDeclaration(
            node,
            Visitors.VisitNode(factory, store.Name(node), visitor),
            default,
            default,
            Visitors.VisitNode(factory, store.Initializer(node), visitor));
        if (!store.Type(node).IsNone)
        {
            FactoryEmitNode.SetTypeNode(store, store.Name(updated), store.Type(node));
        }
        return updated;
    }

    private Node VisitParenthesizedExpression(Node node)
    {
        var innerExpression = FactoryUtilities.SkipOuterExpressions(store, store.Expression(node), ~(OuterExpressionKinds.Assertions | OuterExpressionKinds.ExpressionsWithTypeArguments));
        if (store.Kind(innerExpression) is SyntaxKind.TypeAssertionExpression or SyntaxKind.AsExpression or SyntaxKind.SatisfiesExpression)
        {
            // Make sure we consider all nested cast expressions, e.g.:
            // (<any><number><any>-A).x;
            var expression = Visitors.VisitNode(factory, store.Expression(node), visitor);
            Debug.Assert(!expression.IsNone);

            // We have an expression of the form: (<Type>SubExpr). Emitting this as (SubExpr)
            // is really not desirable. We would like to emit the subexpression as-is. Omitting
            // the parentheses, however, could cause change in the semantics of the generated
            // code if the casted expression has a lower precedence than the rest of the
            // expression.
            //
            // To preserve comments, we return a "PartiallyEmittedExpression" here which will
            // preserve the position information of the original expression.
            //
            // Due to the auto-parenthesization rules used by the visitor and factory functions
            // we can safely elide the parentheses here, as a new synthetic
            // ParenthesizedExpression will be inserted if we remove parentheses too
            // aggressively.
            //
            // If there are leading comments on the expression itself, the emitter will handle ASI
            // for return, throw, and yield by re-introducing parenthesis during emit on an as-need
            // basis.
            return factory.CreatePartiallyEmittedExpression(expression, node);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitAssertionExpression(Node node)
    {
        var expression = Visitors.VisitNode(factory, store.Expression(node), visitor);
        Debug.Assert(!expression.IsNone);
        return factory.CreatePartiallyEmittedExpression(expression, node);
    }

    private Node VisitNonNullExpression(Node node)
    {
        var expression = Visitors.VisitNode(factory, store.Expression(node), visitor);
        Debug.Assert(!expression.IsNone);
        return factory.CreatePartiallyEmittedExpression(expression, node);
    }

    private Node VisitSatisfiesExpression(Node node)
    {
        var expression = Visitors.VisitNode(factory, store.Expression(node), visitor);
        Debug.Assert(!expression.IsNone);
        return factory.CreatePartiallyEmittedExpression(expression, node);
    }

    private Node VisitCallExpression(Node node)
    {
        return factory.UpdateCallExpression(
            node,
            Visitors.VisitNode(factory, store.Expression(node), visitor),
            default,
            Visitors.VisitNodes(factory, store.Arguments(node), visitor));
    }

    private Node VisitNewExpression(Node node)
    {
        return factory.UpdateNewExpression(
            node,
            Visitors.VisitNode(factory, store.Expression(node), visitor),
            default,
            Visitors.VisitNodes(factory, store.Arguments(node), visitor));
    }

    private Node VisitTaggedTemplateExpression(Node node)
    {
        return factory.UpdateTaggedTemplateExpression(
            node,
            Visitors.VisitNode(factory, store.Tag(node), visitor),
            default,
            Visitors.VisitNode(factory, store.Template(node), visitor));
    }

    private Node VisitJsxSelfClosingElement(Node node)
    {
        return factory.UpdateJsxSelfClosingElement(
            node,
            Visitors.VisitNode(factory, store.TagName(node), visitor),
            default,
            Visitors.VisitNode(factory, store.Attributes(node), visitor));
    }

    private Node VisitJsxJsxOpeningElement(Node node)
    {
        return factory.UpdateJsxOpeningElement(
            node,
            Visitors.VisitNode(factory, store.TagName(node), visitor),
            default,
            Visitors.VisitNode(factory, store.Attributes(node), visitor));
    }

    private void RecordEmittedDeclarationInScope(Node node)
    {
        currentScopeFirstDeclarationsOfName ??= new Dictionary<string, Node>(StringComparer.Ordinal);

        var name = DeclaredNameInScope(node);
        currentScopeFirstDeclarationsOfName.TryAdd(name, node);
    }

    private string DeclaredNameInScope(Node node)
    {
        Debug.Assert(store.Kind(store.Name(node)) == SyntaxKind.Identifier);
        return store.Texts.GetString(store.EscapedText(store.Name(node)))!;
    }

    //// Imports and exports

    /// <summary>Visits an import declaration, eliding it if it is type-only or if it has an import clause that may be elided.</summary>
    private VisitResult VisitImportDeclaration(Node node)
    {
        var importClause = store.ImportClause(node);
        if (importClause.IsNone)
        {
            // Do not elide a side-effect only import declaration.
            //  import "foo";
            return node;
        }
        if (store.PhaseModifier(importClause) == SyntaxKind.TypeKeyword)
        {
            // Always elide type-only imports
            return VisitResult.None;
        }

        // Elide the declaration if the import clause was elided.
        var visitedClause = Visitors.VisitNode(factory, importClause, VisitImportClause);
        return !visitedClause.IsNone
            ? factory.UpdateImportDeclaration(
                node,
                default,
                visitedClause,
                store.ModuleSpecifier(node),
                store.Attributes(node))
            : VisitResult.None;
    }

    /// <summary>Visits an import clause, eliding it if its `name` and `namedBindings` may both be elided.</summary>
    private VisitResult VisitImportClause(Node node)
    {
        Debug.Assert(store.PhaseModifier(node) != SyntaxKind.TypeKeyword);
        // Elide the import clause if we elide both its name and its named bindings.
        var name = ShouldEmitAliasDeclaration(node) ? store.Name(node) : Node.None;
        var namedBindings = Visitors.VisitNode(factory, store.NamedBindings(node), VisitNamedImportBindings);
        var phaseModifier = store.PhaseModifier(node);
        return !name.IsNone || !namedBindings.IsNone
            ? factory.UpdateImportClause(node, phaseModifier == SyntaxKind.Unknown ? null : phaseModifier, name, namedBindings)
            : VisitResult.None;
    }

    /// <summary>Visits named import bindings, eliding them if their targets, their references, and the compilation settings allow.</summary>
    private VisitResult VisitNamedImportBindings(Node node)
    {
        if (store.Kind(node) == SyntaxKind.NamespaceImport)
        {
            // Elide a namespace import if it is not referenced.
            return ShouldEmitAliasDeclaration(node) ? node : VisitResult.None;
        }
        else
        {
            // Elide named imports if all of its import specifiers are elided and settings allow.
            var allowEmpty = compilerOptions.VerbatimModuleSyntax == true;
            var elements = Visitors.VisitNodes(factory, store.Elements(node), VisitImportSpecifier);
            return allowEmpty || store.Length(elements) > 0 ? factory.UpdateNamedImports(node, elements) : VisitResult.None;
        }
    }

    /// <summary>Visits an import specifier, eliding it if its target, its references, and the compilation settings allow.</summary>
    private VisitResult VisitImportSpecifier(Node node)
    {
        return !store.IsTypeOnly(node) && ShouldEmitAliasDeclaration(node) ? node : VisitResult.None;
    }

    /// <summary>Visits an export assignment, eliding it if it does not contain a clause that resolves to a value.</summary>
    private VisitResult VisitExportAssignment(Node node)
    {
        // Elide the export assignment if it does not reference a value.
        return compilerOptions.VerbatimModuleSyntax == true || resolver.IsValueAliasDeclaration(node)
            ? Visitors.VisitEachChild(node, visitor, context)
            : VisitResult.None;
    }

    /// <summary>Visits an export declaration, eliding it if it does not contain a clause that resolves to a value.</summary>
    private VisitResult VisitExportDeclaration(Node node)
    {
        if (store.IsTypeOnly(node))
        {
            return VisitResult.None;
        }

        var exportClause = store.ExportClause(node);
        if (exportClause.IsNone || store.Kind(exportClause) == SyntaxKind.NamespaceExport)
        {
            // never elide `export <whatever> from <whereever>` declarations -
            // they should be kept for sideffects/untyped exports, even when the
            // type checker doesn't know about any exports
            return factory.UpdateExportDeclaration(
                node,
                store.Modifiers(node),
                store.IsTypeOnly(node),
                exportClause,
                store.ModuleSpecifier(node),
                store.Attributes(node));
        }

        // Elide the export declaration if all of its named exports are elided.
        var allowEmpty = compilerOptions.VerbatimModuleSyntax == true;
        var visitedClause = Visitors.VisitNode(
            factory,
            exportClause,
            bindings => VisitNamedExportBindings(bindings, allowEmpty));

        return !visitedClause.IsNone
            ? factory.UpdateExportDeclaration(
                node,
                default,
                store.IsTypeOnly(node),
                visitedClause,
                store.ModuleSpecifier(node),
                store.Attributes(node))
            : VisitResult.None;
    }

    /// <summary>Visits named exports, eliding it if it does not contain an export specifier that resolves to a value.</summary>
    private VisitResult VisitNamedExports(Node node, bool allowEmpty)
    {
        // Elide the named exports if all of its export specifiers were elided.
        var elements = Visitors.VisitNodes(factory, store.Elements(node), VisitExportSpecifier);
        return allowEmpty || store.Length(elements) > 0 ? factory.UpdateNamedExports(node, elements) : VisitResult.None;
    }

    private Node VisitNamespaceExports(Node node)
    {
        return factory.UpdateNamespaceExport(node, Visitors.VisitNode(factory, store.Name(node), visitor));
    }

    private VisitResult VisitNamedExportBindings(Node node, bool allowEmpty)
    {
        return store.Kind(node) == SyntaxKind.NamespaceExport ? VisitNamespaceExports(node) : VisitNamedExports(node, allowEmpty);
    }

    /// <summary>Visits an export specifier, eliding it if it does not resolve to a value.</summary>
    private VisitResult VisitExportSpecifier(Node node)
    {
        // Elide an export specifier if it does not reference a value.
        return !store.IsTypeOnly(node) && (compilerOptions.VerbatimModuleSyntax == true || resolver.IsValueAliasDeclaration(node)) ? node : VisitResult.None;
    }

    /// <summary>Visits an import equals declaration.</summary>
    private VisitResult VisitImportEqualsDeclaration(Node node)
    {
        // Always elide type-only imports
        if (store.IsTypeOnly(node))
        {
            return VisitResult.None;
        }

        if (Utilities.IsExternalModuleImportEqualsDeclaration(store, node))
        {
            if (!ShouldEmitAliasDeclaration(node))
            {
                return VisitResult.None;
            }
            return Visitors.VisitEachChild(node, visitor, context);
        }

        if (!ShouldEmitImportEqualsDeclaration(node))
        {
            return VisitResult.None;
        }

        // (The `var` statement for an import-equals of an entity name is module D2's; until then the declaration is
        // left as it is, so the unit's .js output differs.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    /// <summary>Whether to emit an import equals declaration.</summary>
    private bool ShouldEmitImportEqualsDeclaration(Node node)
    {
        // preserve old compiler's behavior: emit 'var' for import declaration (even if we do not consider them referenced) when
        // - current file is not external module
        // - import declaration is top level and target is value imported by entity name
        return ShouldEmitAliasDeclaration(node)
            || (!Utilities.IsExternalModule(store, currentSourceFile)
                && resolver.IsTopLevelValueImportEqualsWithEntityName(node));
    }

    /// <summary>Whether the node is exported from a namespace.</summary>
    private bool IsExportOfNamespace(Node node) => !currentNamespace.IsNone && Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export);

    /// <summary>Whether the node is exported from an external module.</summary>
    private bool IsExternalModuleExport(Node node) => currentNamespace.IsNone && Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export);

    /// <summary>Whether the node is a named export from an external module.</summary>
    private bool IsNamedExternalModuleExport(Node node) => IsExternalModuleExport(node) && !Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default);

    /// <summary>Whether the node is the default export of an external module.</summary>
    private bool IsDefaultExternalModuleExport(Node node) => IsExternalModuleExport(node) && Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default);

    private bool ShouldEmitAliasDeclaration(Node node)
    {
        return compilerOptions.VerbatimModuleSyntax == true || Utilities.IsInJSFile(store, node) || resolver.IsReferencedAliasDeclaration(node);
    }

    //// Emit notification and substitution

    /// <summary>Hook for node emit.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        var savedApplicableSubstitutions = applicableSubstitutions;
        var savedCurrentSourceFile = currentSourceFile;

        if (store.Kind(node) == SyntaxKind.SourceFile)
        {
            currentSourceFile = node;
        }

        // (The namespace and enum substitutions are enabled by visitModuleDeclaration and visitEnumDeclaration,
        // module D2.)

        previousOnEmitNode(hint, node, emitCallback);

        applicableSubstitutions = savedApplicableSubstitutions;
        currentSourceFile = savedCurrentSourceFile;
    }

    /// <summary>Hooks node substitutions.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = previousOnSubstituteNode(hint, node);
        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression(node);
        }
        // (isShorthandPropertyAssignment: the namespace export substitution, module D2.)

        return node;
    }

    private Node SubstituteExpression(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Identifier:
                // substituteExpressionIdentifier: trySubstituteNamespaceExportedName (module D2) substitutes only when
                // a namespace or enum substitution is enabled.
                return node;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return SubstituteConstantValue(node);
        }

        return node;
    }

    private static string SafeMultiLineComment(string value) => value.Replace("*/", "*_/", StringComparison.Ordinal);

    private Node SubstituteConstantValue(Node node)
    {
        var constantValue = TryGetConstEnumValue(node);
        if (constantValue is not null)
        {
            // track the constant value on the node for the printer in mayNeedDotDotForPropertyAccess
            FactoryEmitNode.SetConstantValue(store, node, constantValue);
            Node substitute;
            if (constantValue is string text)
            {
                substitute = factory.CreateStringLiteral(store.Texts.Add(text));
            }
            else
            {
                var number = (double)constantValue;
                substitute = number < 0
                    ? factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, factory.CreateNumericLiteral(-number))
                    : factory.CreateNumericLiteral(number);
            }

            if (compilerOptions.RemoveComments != true)
            {
                var originalNode = TransformerUtilities.GetOriginalNode(store, node, n =>store.Kind(n) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression);
                var sourceFile = Utilities.GetSourceFileOfNode(store, originalNode);
                FactoryEmitNode.AddSyntheticTrailingComment(store, substitute, SyntaxKind.MultiLineCommentTrivia, " " + SafeMultiLineComment(Utilities.GetSourceTextOfNodeFromSourceFile(store, sourceFile, originalNode, includeTrivia: false)) + " ");
            }
            return substitute;
        }
        return node;
    }

    private object? TryGetConstEnumValue(Node node)
    {
        if (compilerOptions.GetIsolatedModules())
        {
            return null;
        }

        return store.Kind(node) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression ? resolver.GetConstantValue(node) : null;
    }
}
