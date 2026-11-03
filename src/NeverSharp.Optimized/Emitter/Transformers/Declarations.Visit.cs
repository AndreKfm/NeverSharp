// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/declarations.ts (visitDeclarationSubtree, visitDeclarationStatements,
// transformTopLevelDeclaration and their helpers)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/declarations/transform.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D5 (see Declarations.cs).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class DeclarationsTransformer
{
    private VisitResult VisitDeclarationSubtree(Node input)
    {
        // (shouldStripInternal: stripInternal is not a Phase 4 option)
        if (Utilities.IsDeclaration(store, input))
        {
            if (IsDeclarationAndNotVisible(input))
            {
                return VisitResult.None;
            }
            if (Utilities.HasDynamicName(store, input))
            {
                // (isolatedDeclarations: not a Phase 4 option)
                if (!resolver.IsLateBound(UtilitiesPublic.GetParseTreeNode(store, input)) || !Utilities.IsEntityNameExpression(store, store.Expression(store.Name(input))))
                {
                    return VisitResult.None;
                }
            }
        }

        // Elide implementation signatures from overload sets
        if (Utilities.IsFunctionLike(store, input) && resolver.IsImplementationOfOverload(input))
        {
            return VisitResult.None;
        }

        // Elide semicolon class statements
        if (store.Kind(input) == SyntaxKind.SemicolonClassElement)
        {
            return VisitResult.None;
        }

        var previousEnclosingDeclaration = Node.None;
        var isEnclosing = IsEnclosingDeclaration(input);
        if (isEnclosing)
        {
            previousEnclosingDeclaration = enclosingDeclaration;
            enclosingDeclaration = input;
        }
        var oldDiag = getSymbolAccessibilityDiagnostic;

        // Setup diagnostic-related flags before first potential `cleanup` call, otherwise
        // We'd see a TDZ violation at runtime
        var canProduceDiagnostic = CanProduceDiagnostics(input);
        var oldWithinObjectLiteralType = suppressNewDiagnosticContexts;
        var shouldEnterSuppressNewDiagnosticsContextContext = store.Kind(input) is SyntaxKind.TypeLiteral or SyntaxKind.MappedType && store.Kind(store.Parent(input)) != SyntaxKind.TypeAliasDeclaration;

        // Emit methods which are private as properties with no type information
        if (store.Kind(input) is SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature)
        {
            if (HasEffectiveModifier(input, ModifierFlags.Private))
            {
                var symbol = resolver.GetSymbolOfDeclaration(input);
                if (!symbol.IsNone && resolver.DeclarationsOf(symbol) is { Count: > 0 } declarations && declarations[0] != resolver.ToParseTree(input))
                {
                    return VisitResult.None; // Elide all but the first overload
                }
                return Cleanup(factory.CreatePropertyDeclaration(EnsureModifiers(input), store.Name(input), Node.None, Node.None, Node.None));
            }
        }

        if (canProduceDiagnostic && !suppressNewDiagnosticContexts)
        {
            getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(input);
        }

        if (store.Kind(input) == SyntaxKind.TypeQuery)
        {
            CheckEntityNameVisibility(store.ExprName(input), enclosingDeclaration);
        }

        if (shouldEnterSuppressNewDiagnosticsContextContext)
        {
            // We stop making new diagnostic contexts within object literal types. Unless it's an object type on the RHS of a type alias declaration. Then we do.
            suppressNewDiagnosticContexts = true;
        }

        if (IsProcessedComponent(store.Kind(input)))
        {
            switch (store.Kind(input))
            {
                case SyntaxKind.ExpressionWithTypeArguments:
                {
                    var expression = store.Expression(input);
                    if (store.Kind(expression) is SyntaxKind.Identifier or SyntaxKind.QualifiedName || Utilities.IsEntityNameExpression(store, expression))
                    {
                        CheckEntityNameVisibility(expression, enclosingDeclaration);
                    }
                    var node = Visitors.VisitEachChild(input, visitDeclarationSubtree, context);
                    return Cleanup(factory.UpdateExpressionWithTypeArguments(node, store.Expression(node), store.TypeArguments(node)));
                }
                case SyntaxKind.TypeReference:
                {
                    CheckEntityNameVisibility(store.TypeName(input), enclosingDeclaration);
                    var node = Visitors.VisitEachChild(input, visitDeclarationSubtree, context);
                    return Cleanup(factory.UpdateTypeReferenceNode(node, store.TypeName(node), store.TypeArguments(node)));
                }
                case SyntaxKind.ConstructSignature:
                    return Cleanup(factory.UpdateConstructSignature(
                        input,
                        EnsureTypeParams(input, store.TypeParameters(input)),
                        UpdateParamsList(input, store.Parameters(input)),
                        EnsureType(input)));
                case SyntaxKind.Constructor:
                {
                    // A constructor declaration may not have a type annotation
                    var ctor = factory.CreateConstructorDeclaration(
                        /*modifiers*/ EnsureModifiers(input),
                        UpdateParamsList(input, store.Parameters(input), ModifierFlags.None),
                        /*body*/ Node.None);
                    return Cleanup(ctor);
                }
                case SyntaxKind.MethodDeclaration:
                {
                    if (store.Kind(store.Name(input)) == SyntaxKind.PrivateIdentifier)
                    {
                        return Cleanup(Node.None);
                    }
                    var sig = factory.CreateMethodDeclaration(
                        EnsureModifiers(input),
                        /*asteriskToken*/ Node.None,
                        store.Name(input),
                        store.QuestionToken(input),
                        EnsureTypeParams(input, store.TypeParameters(input)),
                        UpdateParamsList(input, store.Parameters(input)),
                        EnsureType(input),
                        /*body*/ Node.None);
                    return Cleanup(sig);
                }
                case SyntaxKind.GetAccessor:
                {
                    if (store.Kind(store.Name(input)) == SyntaxKind.PrivateIdentifier)
                    {
                        return Cleanup(Node.None);
                    }
                    return Cleanup(factory.UpdateGetAccessorDeclaration(
                        input,
                        EnsureModifiers(input),
                        store.Name(input),
                        UpdateAccessorParamsList(input, HasEffectiveModifier(input, ModifierFlags.Private)),
                        EnsureType(input),
                        /*body*/ Node.None));
                }
                case SyntaxKind.SetAccessor:
                {
                    if (store.Kind(store.Name(input)) == SyntaxKind.PrivateIdentifier)
                    {
                        return Cleanup(Node.None);
                    }
                    return Cleanup(factory.UpdateSetAccessorDeclaration(
                        input,
                        EnsureModifiers(input),
                        store.Name(input),
                        UpdateAccessorParamsList(input, HasEffectiveModifier(input, ModifierFlags.Private)),
                        /*body*/ Node.None));
                }
                case SyntaxKind.PropertyDeclaration:
                    if (store.Kind(store.Name(input)) == SyntaxKind.PrivateIdentifier)
                    {
                        return Cleanup(Node.None);
                    }
                    return Cleanup(factory.UpdatePropertyDeclaration(
                        input,
                        EnsureModifiers(input),
                        store.Name(input),
                        store.QuestionToken(input),
                        EnsureType(input),
                        EnsureNoInitializer(input)));
                case SyntaxKind.PropertySignature:
                    if (store.Kind(store.Name(input)) == SyntaxKind.PrivateIdentifier)
                    {
                        return Cleanup(Node.None);
                    }
                    return Cleanup(factory.UpdatePropertySignature(
                        input,
                        EnsureModifiers(input),
                        store.Name(input),
                        store.QuestionToken(input),
                        EnsureType(input)));
                case SyntaxKind.MethodSignature:
                {
                    if (store.Kind(store.Name(input)) == SyntaxKind.PrivateIdentifier)
                    {
                        return Cleanup(Node.None);
                    }
                    return Cleanup(factory.UpdateMethodSignature(
                        input,
                        EnsureModifiers(input),
                        store.Name(input),
                        store.QuestionToken(input),
                        EnsureTypeParams(input, store.TypeParameters(input)),
                        UpdateParamsList(input, store.Parameters(input)),
                        EnsureType(input)));
                }
                case SyntaxKind.CallSignature:
                    return Cleanup(factory.UpdateCallSignature(
                        input,
                        EnsureTypeParams(input, store.TypeParameters(input)),
                        UpdateParamsList(input, store.Parameters(input)),
                        EnsureType(input)));
                case SyntaxKind.IndexSignature:
                {
                    var type = Visitors.VisitNode(factory, store.Type(input), visitDeclarationSubtree);
                    return Cleanup(factory.UpdateIndexSignature(
                        input,
                        EnsureModifiers(input),
                        UpdateParamsList(input, store.Parameters(input)),
                        !type.IsNone ? type : factory.CreateToken(SyntaxKind.AnyKeyword)));
                }
                case SyntaxKind.VariableDeclaration:
                {
                    if (store.Kind(store.Name(input)) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
                    {
                        return RecreateBindingPattern(store.Name(input));
                    }
                    shouldEnterSuppressNewDiagnosticsContextContext = true;
                    suppressNewDiagnosticContexts = true; // Variable declaration types also suppress new diagnostic contexts, provided the contexts wouldn't be made for binding pattern types
                    return Cleanup(factory.UpdateVariableDeclaration(input, store.Name(input), /*exclamationToken*/ Node.None, EnsureType(input), EnsureNoInitializer(input)));
                }
                case SyntaxKind.TypeParameter:
                {
                    if (IsPrivateMethodTypeParameter(input) && (!store.Default(input).IsNone || !store.Constraint(input).IsNone))
                    {
                        return Cleanup(factory.UpdateTypeParameterDeclaration(input, store.Modifiers(input), store.Name(input), /*constraint*/ Node.None, /*defaultType*/ Node.None));
                    }
                    return Cleanup(Visitors.VisitEachChild(input, visitDeclarationSubtree, context));
                }
                case SyntaxKind.ConditionalType:
                {
                    // We have to process conditional types in a special way because for visibility purposes we need to push a new enclosingDeclaration
                    // just for the `infer` types in the true branch. It's an implicit declaration scope that only applies to _part_ of the type.
                    var checkType = Visitors.VisitNode(factory, store.CheckType(input), visitDeclarationSubtree);
                    var extendsType = Visitors.VisitNode(factory, store.ExtendsType(input), visitDeclarationSubtree);
                    var oldEnclosingDecl = enclosingDeclaration;
                    enclosingDeclaration = store.TrueType(input);
                    var trueType = Visitors.VisitNode(factory, store.TrueType(input), visitDeclarationSubtree);
                    enclosingDeclaration = oldEnclosingDecl;
                    var falseType = Visitors.VisitNode(factory, store.FalseType(input), visitDeclarationSubtree);
                    return Cleanup(factory.UpdateConditionalTypeNode(input, checkType, extendsType, trueType, falseType));
                }
                case SyntaxKind.FunctionType:
                    return Cleanup(factory.UpdateFunctionTypeNode(
                        input,
                        Visitors.VisitNodes(factory, store.TypeParameters(input), visitDeclarationSubtree),
                        UpdateParamsList(input, store.Parameters(input)),
                        Visitors.VisitNode(factory, store.Type(input), visitDeclarationSubtree)));
                case SyntaxKind.ConstructorType:
                    return Cleanup(factory.UpdateConstructorTypeNode(
                        input,
                        EnsureModifiers(input),
                        Visitors.VisitNodes(factory, store.TypeParameters(input), visitDeclarationSubtree),
                        UpdateParamsList(input, store.Parameters(input)),
                        Visitors.VisitNode(factory, store.Type(input), visitDeclarationSubtree)));
                case SyntaxKind.ImportType:
                {
                    if (!IsLiteralImportTypeNode(input))
                    {
                        return Cleanup(input);
                    }
                    var argument = store.Argument(input);
                    return Cleanup(factory.UpdateImportTypeNode(
                        input,
                        factory.UpdateLiteralTypeNode(argument, RewriteModuleSpecifier(input, store.Literal(argument))),
                        store.Attributes(input),
                        store.Qualifier(input),
                        Visitors.VisitNodes(factory, store.TypeArguments(input), visitDeclarationSubtree),
                        store.IsTypeOf(input)));
                }
                default:
                    throw new InvalidOperationException("Debug Failure. Attempted to process unhandled node kind: " + store.Kind(input));
            }
        }

        if (store.Kind(input) == SyntaxKind.TupleType && GetLineOfPosition(store.Pos(input)) == GetLineOfPosition(store.End(input)))
        {
            FactoryEmitNode.SetEmitFlags(store, input, EmitFlags.SingleLine);
        }

        return Cleanup(Visitors.VisitEachChild(input, visitDeclarationSubtree, context));

        VisitResult Cleanup(Node returnValue)
        {
            if (!returnValue.IsNone && canProduceDiagnostic && Utilities.HasDynamicName(store, input))
            {
                CheckName(input);
            }
            if (isEnclosing)
            {
                enclosingDeclaration = previousEnclosingDeclaration;
            }
            if (canProduceDiagnostic && !suppressNewDiagnosticContexts)
            {
                getSymbolAccessibilityDiagnostic = oldDiag;
            }
            if (shouldEnterSuppressNewDiagnosticsContextContext)
            {
                suppressNewDiagnosticContexts = oldWithinObjectLiteralType;
            }
            if (returnValue == input)
            {
                return returnValue;
            }
            return returnValue.IsNone ? VisitResult.None : NodeFactory.SetOriginalNode(store, PreserveJsDoc(returnValue, input), input);
        }
    }

    // utilities.ts isLiteralImportTypeNode
    private bool IsLiteralImportTypeNode(Node node)
    {
        var argument = store.Argument(node);
        return store.Kind(argument) == SyntaxKind.LiteralType && store.Kind(store.Literal(argument)) == SyntaxKind.StringLiteral;
    }

    private int GetLineOfPosition(int position)
    {
        lineStarts ??= Scanner.GetLineStarts(new SourceFileNodeLike(store, currentSourceFile));
        return Scanner.ComputeLineOfPosition(lineStarts, position);
    }

    private IReadOnlyList<int>? lineStarts;

    private bool IsPrivateMethodTypeParameter(Node node)
    {
        var parent = store.Parent(node);
        return store.Kind(parent) == SyntaxKind.MethodDeclaration && HasEffectiveModifier(parent, ModifierFlags.Private);
    }

    private VisitResult VisitDeclarationStatements(Node input)
    {
        if (!IsPreservedDeclarationStatement(store.Kind(input)))
        {
            // return undefined for unmatched kinds to omit them from the tree
            return VisitResult.None;
        }
        // (shouldStripInternal: stripInternal is not a Phase 4 option)

        switch (store.Kind(input))
        {
            case SyntaxKind.ExportDeclaration:
            {
                if (store.Kind(store.Parent(input)) == SyntaxKind.SourceFile)
                {
                    resultHasExternalModuleIndicator = true;
                }
                resultHasScopeMarker = true;
                // Rewrite external module names if necessary
                return factory.UpdateExportDeclaration(
                    input,
                    store.Modifiers(input),
                    store.IsTypeOnly(input),
                    store.ExportClause(input),
                    RewriteModuleSpecifier(input, store.ModuleSpecifier(input)),
                    TryGetResolutionModeOverride(store.Attributes(input)));
            }
            case SyntaxKind.ExportAssignment:
            {
                // Always visible if the parent node isn't dropped for being not visible
                if (store.Kind(store.Parent(input)) == SyntaxKind.SourceFile)
                {
                    resultHasExternalModuleIndicator = true;
                }
                resultHasScopeMarker = true;
                if (store.Kind(store.Expression(input)) == SyntaxKind.Identifier)
                {
                    return input;
                }
                var newId = factory.CreateUniqueName("_default", GeneratedIdentifierFlags.Optimistic);
                getSymbolAccessibilityDiagnostic = _ => new SymbolAccessibilityDiagnostic
                {
                    DiagnosticMessage = Diagnostics.Default_export_of_the_module_has_or_is_using_private_name_0,
                    ErrorNode = input,
                };
                errorFallbackNode = input;
                var type = EnsureType(input);
                var varDecl = factory.CreateVariableDeclaration(newId, /*exclamationToken*/ Node.None, type, /*initializer*/ Node.None);
                errorFallbackNode = Node.None;
                var statement = factory.CreateVariableStatement(
                    needsDeclare ? NodeArray(factory.CreateToken(SyntaxKind.DeclareKeyword)) : NodeArray(),
                    factory.CreateVariableDeclarationList(NodeArray(varDecl), NodeFlags.Const));

                PreserveJsDoc(statement, input);
                FactoryEmitNode.RemoveAllComments(store, input);
                return VisitResult.Many([statement, factory.UpdateExportAssignment(input, store.Modifiers(input), newId)]);
            }
        }

        var result = TransformTopLevelDeclaration(input);
        // Don't actually transform yet; just leave as original node - will be elided/swapped by late pass
        lateStatementReplacementMap[OriginalNodeKey(input)] = result;
        return input;
    }

    private Node StripExportModifiers(Node statement)
    {
        if (store.Kind(statement) == SyntaxKind.ImportEqualsDeclaration || HasEffectiveModifier(statement, ModifierFlags.Default) || !FactoryUtilitiesPublic.CanHaveModifiers(store, statement))
        {
            // `export import` statements should remain as-is, as imports are _not_ implicitly exported in an ambient namespace
            // Likewise, `export default` classes and the like and just be `default`, so we preserve their `export` modifiers, too
            return statement;
        }

        var modifiers = factory.CreateModifiersFromModifierFlags(Utilities.GetEffectiveModifierFlags(store, statement) & (ModifierFlags.All ^ ModifierFlags.Export));
        return factory.ReplaceModifiers(statement, modifiers);
    }

    private Node UpdateModuleDeclarationAndKeyword(Node node, NodeList modifiers, Node name, Node body)
    {
        var updated = factory.UpdateModuleDeclaration(node, modifiers, name, body);

        if (Utilities.IsAmbientModule(store, updated) || (store.Flags(updated) & NodeFlags.Namespace) != 0)
        {
            return updated;
        }

        var fixedNode = factory.CreateModuleDeclaration(
            store.Modifiers(updated),
            store.Name(updated),
            store.Body(updated),
            store.Flags(updated) | NodeFlags.Namespace);

        NodeFactory.SetOriginalNode(store, fixedNode, updated);
        FactoryUtilitiesPublic.SetTextRange(store, fixedNode, updated);

        return fixedNode;
    }

    private VisitResult TransformTopLevelDeclaration(Node input)
    {
        if (lateMarkedStatements is not null)
        {
            while (lateMarkedStatements.Remove(input))
            {
            }
        }
        // (shouldStripInternal: stripInternal is not a Phase 4 option)
        switch (store.Kind(input))
        {
            case SyntaxKind.ImportEqualsDeclaration:
                return TransformImportEqualsDeclaration(input);
            case SyntaxKind.ImportDeclaration:
                return TransformImportDeclaration(input);
        }
        if (Utilities.IsDeclaration(store, input) && IsDeclarationAndNotVisible(input))
        {
            return VisitResult.None;
        }

        if (store.Kind(input) == SyntaxKind.JSDocImportTag)
        {
            return VisitResult.None;
        }

        // Elide implementation signatures from overload sets
        if (Utilities.IsFunctionLike(store, input) && resolver.IsImplementationOfOverload(input))
        {
            return VisitResult.None;
        }

        var previousEnclosingDeclaration = Node.None;
        var isEnclosing = IsEnclosingDeclaration(input);
        if (isEnclosing)
        {
            previousEnclosingDeclaration = enclosingDeclaration;
            enclosingDeclaration = input;
        }

        var canProdiceDiagnostic = CanProduceDiagnostics(input);
        var oldDiag = getSymbolAccessibilityDiagnostic;
        if (canProdiceDiagnostic)
        {
            getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(input);
        }

        var previousNeedsDeclare = needsDeclare;
        switch (store.Kind(input))
        {
            case SyntaxKind.TypeAliasDeclaration:
            {
                needsDeclare = false;
                var clean = Cleanup(factory.UpdateTypeAliasDeclaration(
                    input,
                    EnsureModifiers(input),
                    store.Name(input),
                    Visitors.VisitNodes(factory, store.TypeParameters(input), visitDeclarationSubtree),
                    Visitors.VisitNode(factory, store.Type(input), visitDeclarationSubtree)));
                needsDeclare = previousNeedsDeclare;
                return clean;
            }
            case SyntaxKind.InterfaceDeclaration:
                return Cleanup(factory.UpdateInterfaceDeclaration(
                    input,
                    EnsureModifiers(input),
                    store.Name(input),
                    EnsureTypeParams(input, store.TypeParameters(input)),
                    TransformHeritageClauses(store.HeritageClauses(input)),
                    Visitors.VisitNodes(factory, store.Members(input), visitDeclarationSubtree)));
            case SyntaxKind.FunctionDeclaration:
                return TransformFunctionDeclaration(input, oldDiag, Cleanup);
            case SyntaxKind.ModuleDeclaration:
            {
                needsDeclare = false;
                var inner = store.Body(input);
                if (!inner.IsNone && store.Kind(inner) == SyntaxKind.ModuleBlock)
                {
                    var oldNeedsScopeFix = needsScopeFixMarker;
                    var oldHasScopeFix = resultHasScopeMarker;
                    resultHasScopeMarker = false;
                    needsScopeFixMarker = false;
                    var statements = Visitors.VisitNodes(factory, store.Statements(inner), visitDeclarationStatements);
                    var lateStatements = TransformAndReplaceLatePaintedStatements(statements);
                    if ((store.Flags(input) & NodeFlags.Ambient) != 0)
                    {
                        needsScopeFixMarker = false; // If it was `declare`'d everything is implicitly exported already, ignore late printed "privates"
                    }
                    // With the final list of statements, there are 3 possibilities:
                    // 1. There's an export assignment or export declaration in the namespace - do nothing
                    // 2. Everything is exported and there are no export assignments or export declarations - strip all export modifiers
                    // 3. Some things are exported, some are not, and there's no marker - add an empty marker
                    if (!Utilities.IsGlobalScopeAugmentation(store, input) && !HasScopeMarker(lateStatements) && !resultHasScopeMarker)
                    {
                        if (needsScopeFixMarker)
                        {
                            var list = new List<Node>(store.Nodes(lateStatements).ToArray()) { CreateEmptyExports() };
                            lateStatements = factory.CreateNodeArray(list);
                        }
                        else
                        {
                            lateStatements = Visitors.VisitNodes(factory, lateStatements, s => StripExportModifiers(s));
                        }
                    }
                    var body = factory.UpdateModuleBlock(inner, lateStatements);
                    needsDeclare = previousNeedsDeclare;
                    needsScopeFixMarker = oldNeedsScopeFix;
                    resultHasScopeMarker = oldHasScopeFix;
                    var mods = EnsureModifiers(input);

                    return Cleanup(UpdateModuleDeclarationAndKeyword(
                        input,
                        mods,
                        IsExternalModuleAugmentation(input) ? RewriteModuleSpecifier(input, store.Name(input)) : store.Name(input),
                        body));
                }
                else
                {
                    needsDeclare = previousNeedsDeclare;
                    var mods = EnsureModifiers(input);
                    needsDeclare = false;
                    Visitors.VisitNode(factory, inner, visitDeclarationStatements);
                    // eagerly transform nested namespaces (the nesting doesn't need any elision or painting done)
                    var id = OriginalNodeKey(inner); // TODO: GH#18217
                    lateStatementReplacementMap.Remove(id, out var body);
                    return Cleanup(UpdateModuleDeclarationAndKeyword(
                        input,
                        mods,
                        store.Name(input),
                        body.Node));
                }
            }
            case SyntaxKind.ClassDeclaration:
                return TransformClassDeclaration(input, Cleanup);
            case SyntaxKind.VariableStatement:
                return Cleanup(TransformVariableStatement(input));
            case SyntaxKind.EnumDeclaration:
            {
                var members = new List<Node>();
                foreach (var m in store.Nodes(store.Members(input)))
                {
                    // (shouldStripInternal: stripInternal is not a Phase 4 option)
                    // Rewrite enum values to their constants, if available
                    var constValue = resolver.GetEnumMemberValue(m);
                    // (isolatedDeclarations: not a Phase 4 option)
                    var newInitializer = constValue switch
                    {
                        null => Node.None,
                        string s => factory.CreateStringLiteral(store.Texts.Add(s)),
                        double d when d < 0 => factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, factory.CreateNumericLiteral(-d)),
                        double d => factory.CreateNumericLiteral(d),
                        _ => Node.None,
                    };
                    members.Add(PreserveJsDoc(factory.UpdateEnumMember(m, store.Name(m), newInitializer), m));
                }
                return Cleanup(factory.UpdateEnumDeclaration(
                    input,
                    EnsureModifiers(input),
                    store.Name(input),
                    factory.CreateNodeArray(members)));
            }
        }
        // Anything left unhandled is an error, so this should be unreachable
        throw new InvalidOperationException("Debug Failure. Unhandled top-level node in declaration emit: " + store.Kind(input));

        VisitResult Cleanup(VisitResult node)
        {
            if (isEnclosing)
            {
                enclosingDeclaration = previousEnclosingDeclaration;
            }
            if (canProdiceDiagnostic)
            {
                getSymbolAccessibilityDiagnostic = oldDiag;
            }
            if (store.Kind(input) == SyntaxKind.ModuleDeclaration)
            {
                needsDeclare = previousNeedsDeclare;
            }
            if (node.Nodes is null && node.Node == input)
            {
                return node;
            }
            errorFallbackNode = Node.None;
            errorNameNode = Node.None;
            if (node.IsNone)
            {
                return node;
            }
            if (node.Nodes is not null)
            {
                return node;
            }
            return NodeFactory.SetOriginalNode(store, PreserveJsDoc(node.Node, input), input);
        }
    }

    // utilities.ts isExternalModuleAugmentation
    private bool IsExternalModuleAugmentation(Node node)
    {
        if (!Utilities.IsAmbientModule(store, node))
        {
            return false;
        }
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.SourceFile:
                return Utilities.IsExternalModule(store, parent);
            case SyntaxKind.ModuleBlock:
            {
                var grandParent = store.Parent(parent);
                return Utilities.IsAmbientModule(store, grandParent) && store.Kind(store.Parent(grandParent)) == SyntaxKind.SourceFile && !Utilities.IsExternalModule(store, store.Parent(grandParent));
            }
        }
        return false;
    }

    private VisitResult TransformFunctionDeclaration(Node input, Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?> oldDiag, Func<VisitResult, VisitResult> cleanup)
    {
        // Generators lose their generator-ness, excepting their return type
        var cleanResult = cleanup(factory.UpdateFunctionDeclaration(
            input,
            EnsureModifiers(input),
            /*asteriskToken*/ Node.None,
            store.Name(input),
            EnsureTypeParams(input, store.TypeParameters(input)),
            UpdateParamsList(input, store.Parameters(input)),
            EnsureType(input),
            /*body*/ Node.None));
        var clean = cleanResult.Node;
        if (!clean.IsNone && resolver.IsExpandoFunctionDeclaration(input) && ShouldEmitFunctionProperties(input))
        {
            var props = resolver.GetPropertiesOfContainerFunction(input);

            // (isolatedDeclarations: not a Phase 4 option)
            // Use parseNodeFactory so it is usable as an enclosing declaration (Track O: a node builder fake scope with the
            // properties as its locals, inside the enclosing declaration)
            var fakeScope = resolver.CreateFakeNamespaceScope(props);
            var exportMappings = new List<(Node Name, string Text)>();
            var declarations = new List<Node>();
            foreach (var p in props)
            {
                var valueDeclaration = resolver.ValueDeclarationOf(p);
                if (!IsExpandoPropertyDeclaration(valueDeclaration))
                {
                    continue;
                }
                var nameStr = Utilities.UnescapeLeadingUnderscores(resolver.NameOf(p));
                if (!Scanner.IsIdentifierText(nameStr, ScriptTarget.ESNext))
                {
                    continue; // unique symbol or non-identifier name - omit, since there's no syntax that can preserve it
                }
                getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(resolver.ToEmit(valueDeclaration));
                var type = resolver.CreateTypeOfDeclaration(valueDeclaration, enclosingDeclaration, fakeScope, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags | InternalNodeBuilderFlags.NoSyntacticPrinter, this);
                if (type.IsNone)
                {
                    type = factory.CreateToken(SyntaxKind.AnyKeyword);
                }
                getSymbolAccessibilityDiagnostic = oldDiag;
                var isNonContextualKeywordName = IsStringANonContextualKeyword(nameStr);
                var name = isNonContextualKeywordName ? factory.GetGeneratedNameForNode(resolver.ToEmit(valueDeclaration)) : factory.CreateIdentifier(nameStr);
                if (isNonContextualKeywordName)
                {
                    exportMappings.Add((name, nameStr));
                }
                var varDecl = factory.CreateVariableDeclaration(name, /*exclamationToken*/ Node.None, type, /*initializer*/ Node.None);
                declarations.Add(factory.CreateVariableStatement(
                    isNonContextualKeywordName ? NodeList.None : NodeArray(factory.CreateToken(SyntaxKind.ExportKeyword)),
                    factory.CreateVariableDeclarationList(NodeArray(varDecl))));
            }
            if (exportMappings.Count == 0)
            {
                for (var i = 0; i < declarations.Count; i++)
                {
                    declarations[i] = factory.ReplaceModifiers(declarations[i], factory.CreateModifiersFromModifierFlags(ModifierFlags.None));
                }
            }
            else
            {
                var specifiers = new List<Node>();
                foreach (var (gen, exp) in exportMappings)
                {
                    specifiers.Add(factory.CreateExportSpecifier(isTypeOnly: false, gen, factory.CreateIdentifier(exp)));
                }
                declarations.Add(factory.CreateExportDeclaration(
                    /*modifiers*/ default,
                    /*isTypeOnly*/ false,
                    factory.CreateNamedExports(factory.CreateNodeArray(specifiers)),
                    default));
            }
            var namespaceDecl = factory.CreateModuleDeclaration(EnsureModifiers(input), store.Name(input), factory.CreateModuleBlock(factory.CreateNodeArray(declarations)), NodeFlags.Namespace);
            if (!HasEffectiveModifier(clean, ModifierFlags.Default))
            {
                return VisitResult.Many([clean, namespaceDecl]);
            }

            var modifiers = factory.CreateModifiersFromModifierFlags((Utilities.GetEffectiveModifierFlags(store, clean) & ~ModifierFlags.ExportDefault) | ModifierFlags.Ambient);
            var cleanDeclaration = factory.UpdateFunctionDeclaration(
                clean,
                modifiers,
                /*asteriskToken*/ Node.None,
                store.Name(clean),
                store.TypeParameters(clean),
                store.Parameters(clean),
                store.Type(clean),
                /*body*/ Node.None);

            var namespaceDeclaration = factory.UpdateModuleDeclaration(
                namespaceDecl,
                modifiers,
                store.Name(namespaceDecl),
                store.Body(namespaceDecl));

            var exportDefaultDeclaration = factory.CreateExportAssignment(
                /*modifiers*/ default,
                /*isExportEquals*/ false,
                store.Name(namespaceDecl));

            if (store.Kind(store.Parent(input)) == SyntaxKind.SourceFile)
            {
                resultHasExternalModuleIndicator = true;
            }
            resultHasScopeMarker = true;

            return VisitResult.Many([cleanDeclaration, namespaceDeclaration, exportDefaultDeclaration]);
        }
        return cleanResult;
    }

    private bool IsExpandoPropertyDeclaration(NodeRef declaration) =>
        !declaration.IsNone && resolver.StoreOf(declaration).Kind(declaration.Node) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.BinaryExpression;

    // utilities.ts isStringANonContextualKeyword
    private static bool IsStringANonContextualKeyword(string name)
    {
        var token = Scanner.StringToToken(name);
        return token is { } t && Utilities.IsKeyword(t) && !(t >= SyntaxKind.FirstContextualKeyword && t <= SyntaxKind.LastContextualKeyword);
    }

    private VisitResult TransformClassDeclaration(Node input, Func<VisitResult, VisitResult> cleanup)
    {
        errorNameNode = store.Name(input);
        errorFallbackNode = input;
        var modifiers = EnsureModifiers(input);
        var typeParameters = EnsureTypeParams(input, store.TypeParameters(input));
        var ctor = TransformerUtilities.GetFirstConstructorWithBody(store, input);
        List<Node>? parameterProperties = null;
        if (!ctor.IsNone)
        {
            var oldDiag = getSymbolAccessibilityDiagnostic;
            foreach (var param in store.Nodes(store.Parameters(ctor)))
            {
                if (!Utilities.HasSyntacticModifier(store, param, ModifierFlags.ParameterPropertyModifier))
                {
                    continue;
                }
                getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(param);
                if (store.Kind(store.Name(param)) == SyntaxKind.Identifier)
                {
                    (parameterProperties ??= []).Add(PreserveJsDoc(
                        factory.CreatePropertyDeclaration(
                            EnsureModifiers(param),
                            store.Name(param),
                            store.QuestionToken(param),
                            EnsureType(param),
                            EnsureNoInitializer(param)),
                        param));
                }
                else
                {
                    // Pattern - this is currently an error, but we emit declarations for it somewhat correctly
                    WalkBindingPattern(param, store.Name(param));
                }
            }
            getSymbolAccessibilityDiagnostic = oldDiag;

            void WalkBindingPattern(Node param, Node pattern)
            {
                foreach (var elem in store.Nodes(store.Elements(pattern)))
                {
                    if (store.Kind(elem) == SyntaxKind.OmittedExpression)
                    {
                        continue;
                    }
                    if (store.Kind(store.Name(elem)) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
                    {
                        WalkBindingPattern(param, store.Name(elem));
                        continue;
                    }
                    (parameterProperties ??= []).Add(factory.CreatePropertyDeclaration(
                        EnsureModifiers(param),
                        store.Name(elem),
                        /*questionOrExclamationToken*/ Node.None,
                        EnsureType(elem),
                        /*initializer*/ Node.None));
                }
            }
        }

        var hasPrivateIdentifier = false;
        foreach (var member in store.Nodes(store.Members(input)))
        {
            var name = Utilities.GetNameOfDeclaration(store, member);
            if (!name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier)
            {
                hasPrivateIdentifier = true;
                break;
            }
        }
        // When the class has at least one private identifier, create a unique constant identifier to retain the nominal typing behavior
        // Prevents other classes with the same public members from being used in place of the current class
        var memberNodes = new List<Node>();
        if (hasPrivateIdentifier)
        {
            memberNodes.Add(factory.CreatePropertyDeclaration(
                /*modifiers*/ NodeList.None,
                factory.CreatePrivateIdentifier(store.Texts.Add("#private")),
                /*questionOrExclamationToken*/ Node.None,
                /*type*/ Node.None,
                /*initializer*/ Node.None));
        }
        var lateIndexes = resolver.CreateLateBoundIndexSignatures(input, enclosingDeclaration, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags, this);
        if (lateIndexes is not null)
        {
            memberNodes.AddRange(lateIndexes);
        }
        if (parameterProperties is not null)
        {
            memberNodes.AddRange(parameterProperties);
        }
        memberNodes.AddRange(store.Nodes(Visitors.VisitNodes(factory, store.Members(input), visitDeclarationSubtree)).ToArray());
        var members = factory.CreateNodeArray(memberNodes);

        var extendsClause = TransformerUtilities.GetEffectiveBaseTypeNode(store, input);
        if (!extendsClause.IsNone && !Utilities.IsEntityNameExpression(store, store.Expression(extendsClause)) && store.Kind(store.Expression(extendsClause)) != SyntaxKind.NullKeyword)
        {
            // We must add a temporary declaration for the extends clause expression

            var oldId = !store.Name(input).IsNone ? Utilities.UnescapeLeadingUnderscores(store.Texts.GetString(store.EscapedText(store.Name(input)))!) : "default";
            var newId = factory.CreateUniqueName(oldId + "_base", GeneratedIdentifierFlags.Optimistic);
            getSymbolAccessibilityDiagnostic = _ => new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = Diagnostics.extends_clause_of_exported_class_0_has_or_is_using_private_name_1,
                ErrorNode = extendsClause,
                TypeName = store.Name(input),
            };
            var extendsType = resolver.CreateTypeOfExpression(store.Expression(extendsClause), input, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags, this);
            var varDecl = factory.CreateVariableDeclaration(newId, /*exclamationToken*/ Node.None, !extendsType.IsNone ? extendsType : factory.CreateToken(SyntaxKind.AnyKeyword), /*initializer*/ Node.None);
            var statement = factory.CreateVariableStatement(
                needsDeclare ? NodeArray(factory.CreateToken(SyntaxKind.DeclareKeyword)) : NodeArray(),
                factory.CreateVariableDeclarationList(NodeArray(varDecl), NodeFlags.Const));
            var heritageClauses = new List<Node>();
            foreach (var clause in store.Nodes(store.HeritageClauses(input)))
            {
                if (store.Token(clause) == SyntaxKind.ExtendsKeyword)
                {
                    var oldDiag = getSymbolAccessibilityDiagnostic;
                    var types = store.Nodes(store.Types(clause));
                    getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(types[0]);
                    var newTypes = new List<Node>();
                    foreach (var t in types)
                    {
                        newTypes.Add(factory.UpdateExpressionWithTypeArguments(t, newId, Visitors.VisitNodes(factory, store.TypeArguments(t), visitDeclarationSubtree)));
                    }
                    var newClause = factory.UpdateHeritageClause(clause, factory.CreateNodeArray(newTypes));
                    getSymbolAccessibilityDiagnostic = oldDiag;
                    heritageClauses.Add(newClause);
                    continue;
                }
                heritageClauses.Add(factory.UpdateHeritageClause(clause, Visitors.VisitNodes(factory, FilterHeritageTypes(clause, allowNull: true), visitDeclarationSubtree)));
            }
            return VisitResult.Many([
                statement,
                cleanup(factory.UpdateClassDeclaration(
                    input,
                    modifiers,
                    store.Name(input),
                    typeParameters,
                    factory.CreateNodeArray(heritageClauses),
                    members)).Node,
            ]); // TODO: GH#18217
        }
        else
        {
            var heritageClauses = TransformHeritageClauses(store.HeritageClauses(input));
            return cleanup(factory.UpdateClassDeclaration(
                input,
                modifiers,
                store.Name(input),
                typeParameters,
                heritageClauses,
                members));
        }
    }

    // factory.createNodeArray(filter(clause.types, t => isEntityNameExpression(t.expression) || (allowNull && t.expression.kind === NullKeyword)))
    // (filter returns the array itself when nothing is removed)
    private NodeList FilterHeritageTypes(Node clause, bool allowNull)
    {
        var types = store.Types(clause);
        var kept = new List<Node>();
        var all = true;
        foreach (var t in store.Nodes(types))
        {
            if (Utilities.IsEntityNameExpression(store, store.Expression(t)) || (allowNull && store.Kind(store.Expression(t)) == SyntaxKind.NullKeyword))
            {
                kept.Add(t);
            }
            else
            {
                all = false;
            }
        }
        return all ? types : factory.CreateNodeArray(kept);
    }

    private Node TransformVariableStatement(Node input)
    {
        var declarationList = store.DeclarationList(input);
        var anyVisible = false;
        foreach (var d in store.Nodes(store.Declarations(declarationList)))
        {
            if (GetBindingNameVisible(d))
            {
                anyVisible = true;
                break;
            }
        }
        if (!anyVisible)
        {
            return Node.None;
        }
        var nodes = Visitors.VisitNodes(factory, store.Declarations(declarationList), visitDeclarationSubtree);
        if (nodes.IsNone || store.Length(nodes) == 0)
        {
            return Node.None;
        }

        var modifiers = EnsureModifiers(input);
        Node declList;
        // isVarUsing / isVarAwaitUsing of the declaration list (getCombinedNodeFlags: the list's flags)
        var blockScoped = store.Flags(declarationList) & NodeFlags.BlockScoped;
        if (blockScoped is NodeFlags.Using or NodeFlags.AwaitUsing)
        {
            declList = factory.CreateVariableDeclarationList(nodes, NodeFlags.Const);
            NodeFactory.SetOriginalNode(store, declList, declarationList);
            FactoryUtilitiesPublic.SetTextRange(store, declList, declarationList);
            FactoryEmitNode.SetCommentRange(store, declList, declarationList);
        }
        else
        {
            declList = factory.UpdateVariableDeclarationList(declarationList, nodes);
        }
        return factory.UpdateVariableStatement(input, modifiers, declList);
    }

    private VisitResult RecreateBindingPattern(Node d)
    {
        var result = new List<Node>();
        foreach (var e in store.Nodes(store.Elements(d)))
        {
            RecreateBindingElement(e, result);
        }
        return VisitResult.Many(result);
    }

    private void RecreateBindingElement(Node e, List<Node> result)
    {
        if (store.Kind(e) == SyntaxKind.OmittedExpression)
        {
            return;
        }
        var name = store.Name(e);
        if (!name.IsNone)
        {
            if (!GetBindingNameVisible(e))
            {
                return;
            }
            if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
            {
                foreach (var inner in store.Nodes(store.Elements(name)))
                {
                    RecreateBindingElement(inner, result);
                }
            }
            else
            {
                result.Add(factory.CreateVariableDeclaration(name, /*exclamationToken*/ Node.None, EnsureType(e), /*initializer*/ Node.None));
            }
        }
    }

    private void CheckName(Node node)
    {
        Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?>? oldDiag = null;
        if (!suppressNewDiagnosticContexts)
        {
            oldDiag = getSymbolAccessibilityDiagnostic;
            getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNodeName(node);
        }
        errorNameNode = store.Name(node);
        var entityName = store.Expression(store.Name(node));
        CheckEntityNameVisibility(entityName, enclosingDeclaration);
        if (!suppressNewDiagnosticContexts)
        {
            getSymbolAccessibilityDiagnostic = oldDiag!;
        }
        errorNameNode = Node.None;
    }

    private bool HasScopeMarker(NodeList statements)
    {
        foreach (var s in store.Nodes(statements))
        {
            if (store.Kind(s) is SyntaxKind.ExportAssignment or SyntaxKind.ExportDeclaration)
            {
                return true;
            }
        }
        return false;
    }

    private NodeList EnsureModifiers(Node node)
    {
        var currentFlags = Utilities.GetEffectiveModifierFlags(store, node);
        var newFlags = EnsureModifierFlags(node);
        if (currentFlags == newFlags)
        {
            // visitArray(node.modifiers, n => tryCast(n, isModifier), isModifier)
            var modifiers = store.Modifiers(node);
            if (modifiers.IsNone)
            {
                return modifiers;
            }
            var all = true;
            var kept = new List<Node>();
            foreach (var m in store.Nodes(modifiers))
            {
                if (UtilitiesPublic.IsModifier(store, m))
                {
                    kept.Add(m);
                }
                else
                {
                    all = false;
                }
            }
            return all ? modifiers : factory.CreateNodeArray(kept);
        }
        return factory.CreateModifiersFromModifierFlags(newFlags);
    }

    private ModifierFlags EnsureModifierFlags(Node node)
    {
        var mask = ModifierFlags.All ^ (ModifierFlags.Public | ModifierFlags.Async | ModifierFlags.Override); // No async and override modifiers in declaration files
        var additions = (needsDeclare && !IsAlwaysType(node)) ? ModifierFlags.Ambient : ModifierFlags.None;
        var parentIsFile = store.Kind(store.Parent(node)) == SyntaxKind.SourceFile;
        if (!parentIsFile) // (|| isBundledEmit && ...: no bundles)
        {
            mask ^= ModifierFlags.Ambient;
            additions = ModifierFlags.None;
        }
        return MaskModifierFlags(node, mask, additions);
    }

    private NodeList TransformHeritageClauses(NodeList nodes)
    {
        var result = new List<Node>();
        foreach (var clause in store.Nodes(nodes))
        {
            var updated = factory.UpdateHeritageClause(
                clause,
                Visitors.VisitNodes(factory, FilterHeritageTypes(clause, allowNull: store.Token(clause) == SyntaxKind.ExtendsKeyword), visitDeclarationSubtree));
            if (!store.Types(updated).IsNone && store.Length(store.Types(updated)) > 0)
            {
                result.Add(updated);
            }
        }
        return factory.CreateNodeArray(result);
    }

    private static bool IsAlwaysType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.InterfaceDeclaration;

    private bool IsAlwaysType(Node node) => IsAlwaysType(store, node);

    // Elide "public" modifier, as it is the default
    private NodeList MaskModifiers(Node node, ModifierFlags? modifierMask = null, ModifierFlags? modifierAdditions = null) =>
        factory.CreateModifiersFromModifierFlags(MaskModifierFlags(node, modifierMask ?? (ModifierFlags.All ^ ModifierFlags.Public), modifierAdditions ?? ModifierFlags.None));

    private ModifierFlags MaskModifierFlags(Node node, ModifierFlags modifierMask, ModifierFlags modifierAdditions)
    {
        var flags = (Utilities.GetEffectiveModifierFlags(store, node) & modifierMask) | modifierAdditions;
        if ((flags & ModifierFlags.Default) != 0 && (flags & ModifierFlags.Export) == 0)
        {
            // A non-exported default is a nonsequitor - we usually try to remove all export modifiers
            // from statements in ambient declarations; but a default export must retain its export modifier to be syntactically valid
            flags ^= ModifierFlags.Export;
        }
        if ((flags & ModifierFlags.Default) != 0 && (flags & ModifierFlags.Ambient) != 0)
        {
            flags ^= ModifierFlags.Ambient; // `declare` is never required alongside `default` (and would be an error if printed)
        }
        return flags;
    }

    private bool CanHaveLiteralInitializer(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                return !HasEffectiveModifier(node, ModifierFlags.Private);
            case SyntaxKind.Parameter:
            case SyntaxKind.VariableDeclaration:
                return true;
        }
        return false;
    }

    private static bool IsPreservedDeclarationStatement(SyntaxKind kind) => kind is
        SyntaxKind.FunctionDeclaration or SyntaxKind.ModuleDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.InterfaceDeclaration or
        SyntaxKind.ClassDeclaration or SyntaxKind.TypeAliasDeclaration or SyntaxKind.EnumDeclaration or SyntaxKind.VariableStatement or
        SyntaxKind.ImportDeclaration or SyntaxKind.ExportDeclaration or SyntaxKind.ExportAssignment;

    private static bool IsProcessedComponent(SyntaxKind kind) => kind is
        SyntaxKind.ConstructSignature or SyntaxKind.Constructor or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or
        SyntaxKind.SetAccessor or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.MethodSignature or
        SyntaxKind.CallSignature or SyntaxKind.IndexSignature or SyntaxKind.VariableDeclaration or SyntaxKind.TypeParameter or
        SyntaxKind.ExpressionWithTypeArguments or SyntaxKind.TypeReference or SyntaxKind.ConditionalType or SyntaxKind.FunctionType or
        SyntaxKind.ConstructorType or SyntaxKind.ImportType;
}
