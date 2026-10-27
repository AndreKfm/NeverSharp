// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkSourceFile, checkSourceElement, checkSourceElementWorker, checkNodeDeferred,
// checkDeferredNode, checkBlock, checkIfStatement, checkDoStatement, checkWhileStatement, checkForStatement,
// checkForInStatement, checkForOfStatement, checkBreakOrContinueStatement, checkReturnStatement, checkReturnExpression,
// checkWithStatement, checkSwitchStatement, checkLabeledStatement, checkThrowStatement, checkTryStatement,
// checkCatchClause, checkExpressionStatement, checkVariableStatement, checkVariableDeclarationList,
// checkVariableDeclaration, checkBindingElement, checkVariableLikeDeclaration, checkVarDeclaredNamesNotShadowed,
// areDeclarationFlagsIdentical, checkParameter, checkPropertyDeclaration, checkPropertySignature,
// checkUnusedRenamedBindingElements)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (checkSourceFile, checkSourceElementWorker and the statement checks)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C8 (replaces C1's resolution walk). JSDoc comments and tags
// (checkJSDocCommentWorker, the JSDoc node kinds) are module C9's (TODO(C9)). Emit helpers (checkExternalEmitHelpers)
// are not checked: Gate C's target (ES2025) needs none of them.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checkNodeDeferred: the deferred nodes of the file being checked, in deferral order (an insertion-ordered set)
    private readonly List<NodeRef> deferredNodes = [];
    private readonly HashSet<NodeRef> deferredNodeSet = [];
    // checker.ts potentialUnusedRenamedBindingElementsInTypes
    private readonly List<NodeRef> potentialUnusedRenamedBindingElementsInTypes = [];

    /// <summary>checker.ts checkSourceFile / checkSourceFileWorker.</summary>
    private void CheckSourceFile(NodeRef file)
    {
        var store = StoreOf(file);
        // (skipLibCheck and isolated declaration files: Gate C checks everything)
        // Grammar checking
        CheckGrammarSourceFile(file);
        potentialUnusedRenamedBindingElementsInTypes.Clear();
        CheckSourceElements(file, store.Statements(file.Node));
        CheckSourceElement(At(file, store.EndOfFileToken(file.Node)));
        // checkDeferredNodes (nodes deferred while deferred nodes are checked are checked in the same pass)
        for (var i = 0; i < deferredNodes.Count; i++)
        {
            CheckDeferredNode(deferredNodes[i]);
        }
        deferredNodes.Clear();
        deferredNodeSet.Clear();
        if (Utilities.IsExternalOrCommonJsModule(store, file.Node, BindingOf(file.File)))
        {
            RegisterForUnusedIdentifiersCheck(file);
        }
        // checkSourceFileWorker's lazy diagnostics (run immediately, see AddLazyDiagnostic)
        if (!store.IsDeclarationFile(file.Node))
        {
            CheckPotentialUnusedRenamedBindingElementsInTypes();
        }
        if (Utilities.IsExternalOrCommonJsModule(store, file.Node, BindingOf(file.File)))
        {
            CheckExternalModuleExports(file);
        }
    }

    // checker.ts checkSourceFileWorker: the renamed binding elements of function types
    private void CheckPotentialUnusedRenamedBindingElementsInTypes()
    {
        foreach (var node in potentialUnusedRenamedBindingElementsInTypes)
        {
            var store = StoreOf(node);
            var symbol = GetSymbolOfDeclaration(node);
            if (symbol.IsNone || IsReferenced(symbol) == 0)
            {
                var wrappingDeclaration = Utilities.WalkUpBindingElementsAndPatterns(store, node.Node);
                var propertyName = store.PropertyName(node.Node);
                var name = store.Name(node.Node);
                var diagnostic = Error(At(node, name), Diagnostics._0_is_an_unused_renaming_of_1_Did_you_intend_to_use_it_as_a_type_annotation,
                    Utilities.DeclarationNameToString(store, name), Utilities.DeclarationNameToString(store, propertyName));
                if (store.Type(wrappingDeclaration).IsNone)
                {
                    // entire parameter does not have type annotation, suggest adding an annotation
                    var related = CreateRelatedForSpan(node.File, store.End(wrappingDeclaration), 0, Diagnostics.We_can_only_write_a_type_for_0_by_adding_a_type_for_the_entire_parameter_here,
                        Utilities.DeclarationNameToString(store, propertyName));
                    AddRelatedInfo(diagnostic, related);
                }
            }
        }
        potentialUnusedRenamedBindingElementsInTypes.Clear();
    }

    private void CheckSourceElements(NodeRef context, NodeList nodes)
    {
        if (nodes.IsNone)
        {
            return;
        }
        var store = StoreOf(context);
        foreach (var node in store.Nodes(nodes))
        {
            CheckSourceElement(At(context, node));
        }
    }

    // checker.ts checkNodeDeferred
    private void CheckNodeDeferred(NodeRef node)
    {
        if (deferredNodeSet.Add(node))
        {
            deferredNodes.Add(node);
        }
    }

    // checker.ts checkDeferredNode
    private void CheckDeferredNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var saveCurrentNode = currentNode;
        currentNode = node;
        instantiationCount = 0;
        switch (store.Kind(n))
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.Decorator:
            case SyntaxKind.JsxOpeningElement:
                // These node kinds are deferred checked when overload resolution fails
                // To save on work, we ensure the arguments are checked just once, in
                // a deferred way
                ResolveUntypedCall(node);
                break;
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                CheckFunctionExpressionOrObjectLiteralMethodDeferred(node);
                break;
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                CheckAccessorDeclaration(node);
                break;
            case SyntaxKind.ClassExpression:
                // checkClassExpressionDeferred
                CheckSourceElements(node, store.Members(n));
                RegisterForUnusedIdentifiersCheck(node);
                break;
            case SyntaxKind.TypeParameter:
                CheckTypeParameterDeferred(node);
                break;
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxElement:
                CheckJsxNode(node);
                break;
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
            case SyntaxKind.ParenthesizedExpression:
                CheckAssertionDeferred(node);
                break;
            case SyntaxKind.VoidExpression:
                CheckExpression(At(node, store.Expression(n)));
                break;
            case SyntaxKind.BinaryExpression:
                if (store.Kind(store.OperatorToken(n)) == SyntaxKind.InstanceOfKeyword)
                {
                    ResolveUntypedCall(node);
                }
                break;
        }
        currentNode = saveCurrentNode;
    }

    /// <summary>checker.ts checkSourceElement.</summary>
    private void CheckSourceElement(NodeRef node)
    {
        if (node.IsNone)
        {
            return;
        }
        var saveCurrentNode = currentNode;
        var saveWithinUnreachableCode = withinUnreachableCode;
        currentNode = node;
        instantiationCount = 0;
        CheckSourceElementWorker(node);
        currentNode = saveCurrentNode;
        withinUnreachableCode = saveWithinUnreachableCode;
    }

    // checker.ts checkSourceElementWorker
    private void CheckSourceElementWorker(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // (checkJSDocCommentWorker for the JSDoc comments of the node, and the JSDoc tags of JavaScript files: TODO(C9))
        if (compilerOptions.AllowUnreachableCode != true && !withinUnreachableCode && CheckSourceElementUnreachable(node))
        {
            withinUnreachableCode = true;
        }
        switch (store.Kind(n))
        {
            case SyntaxKind.TypeParameter:
                CheckTypeParameter(node);
                return;
            case SyntaxKind.Parameter:
                CheckParameter(node);
                return;
            case SyntaxKind.PropertyDeclaration:
                CheckPropertyDeclaration(node);
                return;
            case SyntaxKind.PropertySignature:
                CheckPropertySignature(node);
                return;
            case SyntaxKind.ConstructorType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.IndexSignature:
                CheckSignatureDeclaration(node);
                return;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                CheckMethodDeclaration(node);
                return;
            case SyntaxKind.ClassStaticBlockDeclaration:
                CheckClassStaticBlockDeclaration(node);
                return;
            case SyntaxKind.Constructor:
                CheckConstructorDeclaration(node);
                return;
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                CheckAccessorDeclaration(node);
                return;
            case SyntaxKind.TypeReference:
                CheckTypeReferenceNode(node);
                return;
            case SyntaxKind.TypePredicate:
                CheckTypePredicate(node);
                return;
            case SyntaxKind.TypeQuery:
                CheckTypeQuery(node);
                return;
            case SyntaxKind.TypeLiteral:
                CheckTypeLiteral(node);
                return;
            case SyntaxKind.ArrayType:
                CheckSourceElement(At(node, store.ElementType(n)));
                return;
            case SyntaxKind.TupleType:
                CheckTupleType(node);
                return;
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
                // checkUnionOrIntersectionType
                CheckSourceElements(node, store.Types(n));
                GetTypeFromTypeNode(node);
                return;
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.OptionalType:
            case SyntaxKind.RestType:
                CheckSourceElement(At(node, store.Type(n)));
                return;
            case SyntaxKind.ThisType:
                GetTypeFromThisTypeNode(node);
                return;
            case SyntaxKind.TypeOperator:
                CheckGrammarTypeOperatorNode(node);
                CheckSourceElement(At(node, store.Type(n)));
                return;
            case SyntaxKind.ConditionalType:
                ForEachChildCheckSourceElement(node);
                return;
            case SyntaxKind.InferType:
                CheckInferType(node);
                return;
            case SyntaxKind.TemplateLiteralType:
                CheckTemplateLiteralType(node);
                return;
            case SyntaxKind.ImportType:
                CheckImportType(node);
                return;
            case SyntaxKind.NamedTupleMember:
                CheckNamedTupleMember(node);
                return;
            case SyntaxKind.JSDocTypeExpression:
                CheckSourceElement(At(node, store.Type(n)));
                return;
            case SyntaxKind.IndexedAccessType:
                // checkIndexedAccessType
                CheckSourceElement(At(node, store.ObjectType(n)));
                CheckSourceElement(At(node, store.IndexType(n)));
                CheckIndexedAccessIndexType(GetTypeFromIndexedAccessTypeNode(node), node);
                return;
            case SyntaxKind.MappedType:
                CheckMappedType(node);
                return;
            case SyntaxKind.FunctionDeclaration:
                CheckFunctionDeclaration(node);
                return;
            case SyntaxKind.Block:
            case SyntaxKind.ModuleBlock:
                CheckBlock(node);
                return;
            case SyntaxKind.VariableStatement:
                CheckVariableStatement(node);
                return;
            case SyntaxKind.ExpressionStatement:
                // checkExpressionStatement
                CheckGrammarStatementInAmbientContext(node);
                CheckExpression(At(node, store.Expression(n)));
                return;
            case SyntaxKind.IfStatement:
                CheckIfStatement(node);
                return;
            case SyntaxKind.DoStatement:
                CheckGrammarStatementInAmbientContext(node);
                CheckSourceElement(At(node, store.Statement(n)));
                CheckTruthinessExpression(At(node, store.Expression(n)));
                return;
            case SyntaxKind.WhileStatement:
                CheckGrammarStatementInAmbientContext(node);
                CheckTruthinessExpression(At(node, store.Expression(n)));
                CheckSourceElement(At(node, store.Statement(n)));
                return;
            case SyntaxKind.ForStatement:
                CheckForStatement(node);
                return;
            case SyntaxKind.ForInStatement:
                CheckForInStatement(node);
                return;
            case SyntaxKind.ForOfStatement:
                CheckForOfStatement(node);
                return;
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.BreakStatement:
                // checkBreakOrContinueStatement
                if (!CheckGrammarStatementInAmbientContext(node))
                {
                    CheckGrammarBreakOrContinueStatement(node);
                }
                return;
            case SyntaxKind.ReturnStatement:
                CheckReturnStatement(node);
                return;
            case SyntaxKind.WithStatement:
                CheckWithStatement(node);
                return;
            case SyntaxKind.SwitchStatement:
                CheckSwitchStatement(node);
                return;
            case SyntaxKind.LabeledStatement:
                CheckLabeledStatement(node);
                return;
            case SyntaxKind.ThrowStatement:
                CheckThrowStatement(node);
                return;
            case SyntaxKind.TryStatement:
                // checkTryStatement
                CheckGrammarStatementInAmbientContext(node);
                CheckBlock(At(node, store.TryBlock(n)));
                if (!store.CatchClause(n).IsNone)
                {
                    CheckCatchClause(At(node, store.CatchClause(n)));
                }
                if (!store.FinallyBlock(n).IsNone)
                {
                    CheckBlock(At(node, store.FinallyBlock(n)));
                }
                return;
            case SyntaxKind.VariableDeclaration:
                CheckGrammarVariableDeclaration(node);
                CheckVariableLikeDeclaration(node);
                return;
            case SyntaxKind.BindingElement:
                CheckGrammarBindingElement(node);
                CheckVariableLikeDeclaration(node);
                return;
            case SyntaxKind.ClassDeclaration:
                CheckClassDeclaration(node);
                return;
            case SyntaxKind.InterfaceDeclaration:
                CheckInterfaceDeclaration(node);
                return;
            case SyntaxKind.TypeAliasDeclaration:
                CheckTypeAliasDeclaration(node);
                return;
            case SyntaxKind.EnumDeclaration:
                CheckEnumDeclaration(node);
                return;
            case SyntaxKind.EnumMember:
                // checkEnumMember
                if (store.Kind(store.Name(n)) == SyntaxKind.PrivateIdentifier)
                {
                    Error(node, Diagnostics.An_enum_member_cannot_be_named_with_a_private_identifier);
                }
                if (!store.Initializer(n).IsNone)
                {
                    CheckExpression(At(node, store.Initializer(n)));
                }
                return;
            case SyntaxKind.ModuleDeclaration:
                CheckModuleDeclaration(node);
                return;
            case SyntaxKind.ImportDeclaration:
                CheckImportDeclaration(node);
                return;
            case SyntaxKind.ImportEqualsDeclaration:
                CheckImportEqualsDeclaration(node);
                return;
            case SyntaxKind.ExportDeclaration:
                CheckExportDeclaration(node);
                return;
            case SyntaxKind.ExportAssignment:
                CheckExportAssignment(node);
                return;
            case SyntaxKind.EmptyStatement:
            case SyntaxKind.DebuggerStatement:
                CheckGrammarStatementInAmbientContext(node);
                return;
            case SyntaxKind.MissingDeclaration:
                CheckDecorators(node);
                return;
        }
    }

    // forEachChild(node, checkSourceElement)
    private void ForEachChildCheckSourceElement(NodeRef node)
    {
        var store = StoreOf(node);
        var children = new List<Node>();
        NodeVisitor.ForEachChild<object>(store, node.Node, child =>
        {
            children.Add(child);
            return null;
        });
        foreach (var child in children)
        {
            CheckSourceElement(At(node, child));
        }
    }

    // checker.ts checkBlock
    private void CheckBlock(NodeRef node)
    {
        var store = StoreOf(node);
        // Grammar checking for SyntaxKind.Block
        if (store.Kind(node.Node) == SyntaxKind.Block)
        {
            CheckGrammarStatementInAmbientContext(node);
        }
        CheckSourceElements(node, store.Statements(node.Node));
        if (LocalsOf(node) is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // checker.ts checkIfStatement
    private void CheckIfStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);
        var expression = At(node, store.Expression(n));
        var type = CheckTruthinessExpression(expression);
        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(expression, type, At(node, store.ThenStatement(n)));
        CheckSourceElement(At(node, store.ThenStatement(n)));
        if (store.Kind(store.ThenStatement(n)) == SyntaxKind.EmptyStatement)
        {
            Error(At(node, store.ThenStatement(n)), Diagnostics.The_body_of_an_if_statement_cannot_be_the_empty_statement);
        }
        CheckSourceElement(At(node, store.ElseStatement(n)));
    }

    // checker.ts checkForStatement
    private void CheckForStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        var initializer = store.Initializer(n);
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if (!initializer.IsNone && store.Kind(initializer) == SyntaxKind.VariableDeclarationList)
            {
                CheckGrammarVariableDeclarationList(At(node, initializer));
            }
        }
        if (!initializer.IsNone)
        {
            if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList)
            {
                CheckVariableDeclarationList(At(node, initializer));
            }
            else
            {
                CheckExpression(At(node, initializer));
            }
        }
        if (!store.Condition(n).IsNone)
        {
            CheckTruthinessExpression(At(node, store.Condition(n)));
        }
        if (!store.Incrementor(n).IsNone)
        {
            CheckExpression(At(node, store.Incrementor(n)));
        }
        CheckSourceElement(At(node, store.Statement(n)));
        if (LocalsOf(node) is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // checker.ts checkForInStatement
    private void CheckForInStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        CheckGrammarForInOrForOfStatement(node);
        var expression = At(node, store.Expression(n));
        var rightType = GetNonNullableTypeIfNeeded(CheckExpression(expression));
        // TypeScript 1.0 spec (April 2014): 5.4
        // In a 'for-in' statement of the form
        // for (let VarDecl in Expr) Statement
        //   VarDecl must be a variable declaration without a type annotation that declares a variable of type Any,
        //   and Expr must be an expression of type Any, an object type, or a type parameter type.
        var initializer = store.Initializer(n);
        if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList)
        {
            var declarations = store.Nodes(store.Declarations(initializer));
            if (declarations.Length != 0 && Utilities.IsBindingPattern(store, store.Name(declarations[0])))
            {
                Error(At(node, store.Name(declarations[0])), Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_destructuring_pattern);
            }
            CheckVariableDeclarationList(At(node, initializer));
        }
        else
        {
            // In a 'for-in' statement of the form
            // for (Var in Expr) Statement
            //   Var must be an expression classified as a reference of type Any or the String primitive type,
            //   and Expr must be an expression of type Any, an object type, or a type parameter type.
            var varExpr = At(node, initializer);
            var leftType = CheckExpression(varExpr);
            if (store.Kind(initializer) is SyntaxKind.ArrayLiteralExpression or SyntaxKind.ObjectLiteralExpression)
            {
                Error(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_destructuring_pattern);
            }
            else if (!IsTypeAssignableTo(GetIndexTypeOrString(rightType), leftType))
            {
                Error(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_must_be_of_type_string_or_any);
            }
            else
            {
                // run check only former check succeeded to avoid cascading errors
                CheckReferenceExpression(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_must_be_a_variable_or_a_property_access,
                    Diagnostics.The_left_hand_side_of_a_for_in_statement_may_not_be_an_optional_property_access);
            }
        }
        // unknownType is returned i.e. if node.expression is identifier whose name cannot be resolved
        // in this case error about missing name is already reported - do not report extra one
        if (rightType == neverType || !IsTypeAssignableToKind(rightType, TypeFlags.NonPrimitive | TypeFlags.InstantiableNonPrimitive))
        {
            Error(expression, Diagnostics.The_right_hand_side_of_a_for_in_statement_must_be_of_type_any_an_object_type_or_a_type_parameter_but_here_has_type_0, TypeToString(rightType));
        }
        CheckSourceElement(At(node, store.Statement(n)));
        if (LocalsOf(node) is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // checker.ts getIndexTypeOrString
    private Type GetIndexTypeOrString(Type type)
    {
        var indexType = GetExtractStringType(GetIndexType(type));
        return (Types.Flags(indexType) & TypeFlags.Never) != 0 ? stringType : indexType;
    }

    // checker.ts checkForOfStatement
    private void CheckForOfStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        CheckGrammarForInOrForOfStatement(node);
        var container = GetContainingFunctionOrClassStaticBlock(store, n);
        if (!store.AwaitModifier(n).IsNone)
        {
            if (!container.IsNone && store.Kind(container) == SyntaxKind.ClassStaticBlockDeclaration)
            {
                GrammarErrorOnNode(At(node, store.AwaitModifier(n)), Diagnostics.for_await_loops_cannot_be_used_inside_a_class_static_block);
            }
            // (for-await-of in an async function prior to ES2018 needs the __asyncValues helper: Gate C targets ES2025)
        }
        // Check the LHS and RHS
        // If the LHS is a declaration, just check it as a variable declaration, which will in turn check the RHS
        // via checkRightHandSideOfForOf.
        // If the LHS is an expression, check the LHS, as a destructuring assignment or as a reference.
        // Then check that the RHS is assignable to it.
        if (store.Kind(store.Initializer(n)) == SyntaxKind.VariableDeclarationList)
        {
            CheckVariableDeclarationList(At(node, store.Initializer(n)));
        }
        else
        {
            CheckForOfStatementExpressions(node);
        }
        CheckSourceElement(At(node, store.Statement(n)));
        if (LocalsOf(node) is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // checkForOfStatement, the left and right hand sides when the left hand side is an expression
    private void CheckForOfStatementExpressions(NodeRef statement)
    {
        var store = StoreOf(statement);
        var initializer = store.Initializer(statement.Node);
        var varExpr = At(statement, initializer);
        var iteratedType = CheckRightHandSideOfForOf(statement);
        // There may be a destructuring assignment on the left side
        if (store.Kind(initializer) is SyntaxKind.ArrayLiteralExpression or SyntaxKind.ObjectLiteralExpression)
        {
            // iteratedType may be undefined. In this case, we still want to check the structure of
            // varExpr, in particular making sure it's a valid LeftHandSideExpression. But we'd like
            // to short circuit the type relation checking as much as possible, so we pass the unknownType.
            CheckDestructuringAssignment(varExpr, iteratedType.IsNone ? errorType : iteratedType);
            return;
        }
        var leftType = CheckExpression(varExpr);
        CheckReferenceExpression(varExpr,
            Diagnostics.The_left_hand_side_of_a_for_of_statement_must_be_a_variable_or_a_property_access,
            Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_an_optional_property_access);
        // iteratedType will be undefined if the rightType was missing properties/signatures
        // required to get its iteratedType (like [Symbol.iterator] or next). This may be
        // because we accessed properties from anyType, or it may have led to an error inside
        // getElementTypeOfIterable.
        if (!iteratedType.IsNone)
        {
            CheckTypeAssignableToAndOptionallyElaborate(iteratedType, leftType, varExpr, At(statement, store.Expression(statement.Node)));
        }
    }

    // utilities.ts getContainingFunctionOrClassStaticBlock
    private static Node GetContainingFunctionOrClassStaticBlock(NodeStore store, Node node)
    {
        for (var current = store.Parent(node); !current.IsNone; current = store.Parent(current))
        {
            if (Utilities.IsFunctionLike(store, current) || store.Kind(current) == SyntaxKind.ClassStaticBlockDeclaration)
            {
                return current;
            }
        }
        return Node.None;
    }

    // checker.ts checkReturnStatement
    private void CheckReturnStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        if (CheckGrammarStatementInAmbientContext(node))
        {
            return;
        }
        var containerNode = GetContainingFunctionOrClassStaticBlock(store, n);
        if (!containerNode.IsNone && store.Kind(containerNode) == SyntaxKind.ClassStaticBlockDeclaration)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_return_statement_cannot_be_used_inside_a_class_static_block);
            return;
        }
        if (containerNode.IsNone)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_return_statement_can_only_be_used_within_a_function_body);
            return;
        }
        var container = At(node, containerNode);
        var signature = GetSignatureFromDeclaration(container);
        var returnType = GetReturnTypeOfSignature(signature);
        var functionFlags = GetFunctionFlagsOf(store, containerNode);
        var exprNode = store.Expression(n);
        if (strictNullChecks || !exprNode.IsNone || (Types.Flags(returnType) & TypeFlags.Never) != 0)
        {
            var exprType = exprNode.IsNone ? undefinedType : CheckExpressionCached(At(node, exprNode));
            if (store.Kind(containerNode) == SyntaxKind.SetAccessor)
            {
                if (!exprNode.IsNone)
                {
                    Error(node, Diagnostics.Setters_cannot_return_a_value);
                }
            }
            else if (store.Kind(containerNode) == SyntaxKind.Constructor)
            {
                if (!exprNode.IsNone && !CheckTypeAssignableToAndOptionallyElaborate(exprType, returnType, node, At(node, exprNode)))
                {
                    Error(node, Diagnostics.Return_type_of_constructor_signature_must_be_assignable_to_the_instance_type_of_the_class);
                }
            }
            else if (!GetReturnTypeFromAnnotation(container).IsNone)
            {
                var unwrappedReturnType = UnwrapReturnType(returnType, functionFlags);
                if (unwrappedReturnType.IsNone)
                {
                    unwrappedReturnType = returnType;
                }
                CheckReturnExpression(container, unwrappedReturnType, node, At(node, exprNode), exprType);
            }
        }
        else if (store.Kind(containerNode) != SyntaxKind.Constructor && compilerOptions.NoImplicitReturns == true && !IsUnwrappedReturnTypeUndefinedVoidOrAny(container, returnType))
        {
            // The function has a return type, but the return statement doesn't have an expression.
            Error(node, Diagnostics.Not_all_code_paths_return_a_value);
        }
    }

    /// <summary>
    /// checker.ts checkReturnExpression. When checking an arrow expression such as `(x) => exp`, then `node` is the
    /// expression `exp`. Otherwise, `node` is a return statement.
    /// </summary>
    internal void CheckReturnExpression(NodeRef container, Type unwrappedReturnType, NodeRef node, NodeRef expr, Type exprType, bool inConditionalExpression = false)
    {
        var unwrappedExprType = exprType;
        var functionFlags = GetFunctionFlagsOf(StoreOf(container), container.Node);
        if (!expr.IsNone)
        {
            var exprStore = StoreOf(expr);
            var unwrappedExpr = Utilities.SkipParentheses(exprStore, expr.Node);
            if (exprStore.Kind(unwrappedExpr) == SyntaxKind.ConditionalExpression)
            {
                var whenTrue = At(expr, exprStore.WhenTrue(unwrappedExpr));
                var whenFalse = At(expr, exprStore.WhenFalse(unwrappedExpr));
                CheckReturnExpression(container, unwrappedReturnType, node, whenTrue, CheckExpression(whenTrue), inConditionalExpression: true);
                CheckReturnExpression(container, unwrappedReturnType, node, whenFalse, CheckExpression(whenFalse), inConditionalExpression: true);
                return;
            }
        }
        var inReturnStatement = Kind(node) == SyntaxKind.ReturnStatement;
        if ((functionFlags & FunctionFlags.Async) != 0)
        {
            unwrappedExprType = CheckAwaitedType(exprType, withAlias: false, node, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
        }
        var effectiveExpr = expr.IsNone ? expr : GetEffectiveCheckNode(expr); // The effective expression for diagnostics purposes.
        var errorNode = inReturnStatement && !inConditionalExpression ? node : effectiveExpr;
        CheckTypeAssignableToAndOptionallyElaborate(unwrappedExprType, unwrappedReturnType, errorNode, effectiveExpr);
    }

    // checker.ts checkWithStatement
    private void CheckWithStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking for withStatement
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if ((store.Flags(n) & NodeFlags.AwaitContext) != 0)
            {
                GrammarErrorOnFirstToken(node, Diagnostics.with_statements_are_not_allowed_in_an_async_function_block);
            }
        }
        CheckExpression(At(node, store.Expression(n)));
        var file = FileAt(node.File);
        if (file.ParseDiagnostics.Count == 0)
        {
            var start = Utilities.GetSpanOfTokenAtPosition(file.Store, file.Root, store.Pos(n)).Start;
            var end = store.Pos(store.Statement(n));
            GrammarErrorAtPos(node, start, end - start, Diagnostics.The_with_statement_is_not_supported_All_symbols_in_a_with_block_will_have_type_any);
        }
    }

    // checker.ts checkSwitchStatement
    private void CheckSwitchStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);
        var firstDefaultClause = Node.None;
        var hasDuplicateDefaultClause = false;
        var expressionType = CheckExpression(At(node, store.Expression(n)));
        var caseBlock = store.CaseBlock(n);
        foreach (var clause in store.Nodes(store.Clauses(caseBlock)))
        {
            // Grammar check for duplicate default clauses, skip if we already report duplicate default clause
            if (store.Kind(clause) == SyntaxKind.DefaultClause && !hasDuplicateDefaultClause)
            {
                if (firstDefaultClause.IsNone)
                {
                    firstDefaultClause = clause;
                }
                else
                {
                    GrammarErrorOnNode(At(node, clause), Diagnostics.A_default_clause_cannot_appear_more_than_once_in_a_switch_statement);
                    hasDuplicateDefaultClause = true;
                }
            }
            if (store.Kind(clause) == SyntaxKind.CaseClause)
            {
                // checkCaseClause (a lazy diagnostic)
                var caseExpression = At(node, store.Expression(clause));
                var caseType = CheckExpression(caseExpression);
                if (!IsTypeEqualityComparableTo(expressionType, caseType))
                {
                    // expressionType is not comparable to caseType, try the reversed check and report errors if it fails
                    CheckTypeComparableTo(caseType, expressionType, caseExpression);
                }
            }
            CheckSourceElements(node, store.Statements(clause));
            if (compilerOptions.NoFallthroughCasesInSwitch == true)
            {
                var fallthrough = FallthroughFlowNodeOf(At(node, clause));
                if (!fallthrough.IsNone && IsReachableFlowNode(fallthrough))
                {
                    Error(At(node, clause), Diagnostics.Fallthrough_case_in_switch);
                }
            }
        }
        if (LocalsOf(At(node, caseBlock)) is not null)
        {
            RegisterForUnusedIdentifiersCheck(At(node, caseBlock));
        }
    }

    // checker.ts checkLabeledStatement
    private void CheckLabeledStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            var label = store.Label(n);
            for (var current = store.Parent(n); !current.IsNone; current = store.Parent(current))
            {
                if (Utilities.IsFunctionLike(store, current))
                {
                    break;
                }
                if (store.Kind(current) == SyntaxKind.LabeledStatement && store.Texts.Equals(store.EscapedText(store.Label(current)), store.Texts.GetString(store.EscapedText(label))!))
                {
                    GrammarErrorOnNode(At(node, label), Diagnostics.Duplicate_label_0, GetTextOfNode(At(node, label)));
                    break;
                }
            }
        }
        CheckUnusedLabel(node); // (module C7)
        CheckSourceElement(At(node, store.Statement(n)));
    }

    // checker.ts checkThrowStatement
    private void CheckThrowStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var expression = store.Expression(n);
        // Grammar checking
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if (store.Kind(expression) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(expression), ""))
            {
                GrammarErrorAtPos(node, store.Pos(expression), 0, Diagnostics.Line_break_not_permitted_here);
            }
        }
        if (!expression.IsNone)
        {
            CheckExpression(At(node, expression));
        }
    }

    // checker.ts checkCatchClause (part of checkTryStatement)
    private void CheckCatchClause(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        var declaration = store.VariableDeclaration(n);
        if (!declaration.IsNone)
        {
            var declarationRef = At(node, declaration);
            CheckVariableLikeDeclaration(declarationRef);
            var typeNode = store.Type(declaration);
            if (!typeNode.IsNone)
            {
                var type = GetTypeFromTypeNode(At(node, typeNode));
                if (!type.IsNone && (Types.Flags(type) & TypeFlags.AnyOrUnknown) == 0)
                {
                    GrammarErrorOnFirstToken(At(node, typeNode), Diagnostics.Catch_clause_variable_type_annotation_must_be_any_or_unknown_if_specified);
                }
            }
            else if (!store.Initializer(declaration).IsNone)
            {
                GrammarErrorOnFirstToken(At(node, store.Initializer(declaration)), Diagnostics.Catch_clause_variable_cannot_have_an_initializer);
            }
            else
            {
                var blockLocals = LocalsOf(At(node, store.Block(n)));
                var catchLocals = LocalsOf(node);
                if (blockLocals is not null && catchLocals is not null)
                {
                    for (var i = 0; i < catchLocals.Count; i++)
                    {
                        var caughtName = catchLocals.NameAt(i);
                        var blockLocal = blockLocals.Get(caughtName);
                        if (!blockLocal.IsNone && !ValueDeclarationOf(blockLocal).IsNone && (SymbolFlagsOf(blockLocal) & SymbolFlags.BlockScopedVariable) != 0)
                        {
                            GrammarErrorOnNode(ValueDeclarationOf(blockLocal), Diagnostics.Cannot_redeclare_identifier_0_in_catch_clause, caughtName);
                        }
                    }
                }
            }
        }
        CheckBlock(At(node, store.Block(n)));
    }

    // checker.ts checkVariableStatement
    private void CheckVariableStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var declarationList = At(node, store.DeclarationList(node.Node));
        // Grammar checking
        if (!CheckGrammarModifiers(node) && !CheckGrammarVariableDeclarationList(declarationList))
        {
            CheckGrammarForDisallowedBlockScopedVariableStatement(node);
        }
        CheckVariableDeclarationList(declarationList);
    }

    // checker.ts checkVariableDeclarationList
    private void CheckVariableDeclarationList(NodeRef node)
    {
        // (using declarations prior to ES2022 need the __addDisposableResource helpers: Gate C targets ES2025)
        CheckSourceElements(node, StoreOf(node).Declarations(node.Node));
    }

    // checker.ts checkParameter
    private void CheckParameter(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        // It is a SyntaxError if the Identifier "eval" or the Identifier "arguments" occurs as the
        // Identifier in a PropertySetParameterList of a PropertyAssignment that is contained in strict code
        // or if its FunctionBody is strict code(11.1.5).
        CheckGrammarModifiers(node);
        CheckVariableLikeDeclaration(node);
        var func = Utilities.GetContainingFunctionOrNone(store, n);
        var name = store.Name(n);
        if (Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.ParameterPropertyModifier))
        {
            // (erasableSyntaxOnly: not an option of Gate C)
            if (!(store.Kind(func) == SyntaxKind.Constructor && !Utilities.NodeIsMissing(store, store.Body(func))))
            {
                Error(node, Diagnostics.A_parameter_property_is_only_allowed_in_a_constructor_implementation);
            }
            if (store.Kind(func) == SyntaxKind.Constructor && store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "constructor"))
            {
                Error(At(node, name), Diagnostics.constructor_cannot_be_used_as_a_parameter_property_name);
            }
        }
        if (store.Initializer(n).IsNone && IsOptionalDeclaration(store, n) && Utilities.IsBindingPattern(store, name) && !store.Body(func).IsNone)
        {
            Error(node, Diagnostics.A_binding_pattern_parameter_cannot_be_optional_in_an_implementation_signature);
        }
        if (!name.IsNone && store.Kind(name) == SyntaxKind.Identifier && (store.Texts.Equals(store.EscapedText(name), "this") || store.Texts.Equals(store.EscapedText(name), "new")))
        {
            var nameText = store.Texts.GetString(store.EscapedText(name))!;
            if (store.Nodes(store.Parameters(func)).IndexOf(n) != 0)
            {
                Error(node, Diagnostics.A_0_parameter_must_be_the_first_parameter, nameText);
            }
            if (store.Kind(func) is SyntaxKind.Constructor or SyntaxKind.ConstructSignature or SyntaxKind.ConstructorType)
            {
                Error(node, Diagnostics.A_constructor_cannot_have_a_this_parameter);
            }
            if (store.Kind(func) == SyntaxKind.ArrowFunction)
            {
                Error(node, Diagnostics.An_arrow_function_cannot_have_a_this_parameter);
            }
            if (store.Kind(func) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
            {
                Error(node, Diagnostics.get_and_set_accessors_cannot_declare_this_parameters);
            }
        }
        // Only check rest parameter type if it's not a binding pattern. Since binding patterns are
        // not allowed in a rest parameter, we already have an error from checkGrammarParameterList.
        if (!store.DotDotDotToken(n).IsNone && !Utilities.IsBindingPattern(store, name) &&
            !IsTypeAssignableTo(GetReducedType(GetTypeOfSymbol(NodeSymbol(node))), anyReadonlyArrayType))
        {
            Error(node, Diagnostics.A_rest_parameter_must_be_of_an_array_type);
        }
    }

    // utilities.ts isOptionalDeclaration (hasQuestionToken)
    private static bool IsOptionalDeclaration(NodeStore store, Node node) => !store.QuestionToken(node).IsNone;

    // checker.ts checkPropertyDeclaration
    private void CheckPropertyDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        if (!CheckGrammarModifiers(node) && !CheckGrammarProperty(node))
        {
            CheckGrammarComputedPropertyName(At(node, store.Name(n)));
        }
        CheckVariableLikeDeclaration(node);
        // (setNodeLinksForPrivateIdentifierScope: emit flags only)
        // property signatures already report "initializer not allowed in ambient context" elsewhere
        if (Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract) && store.Kind(n) == SyntaxKind.PropertyDeclaration && !store.Initializer(n).IsNone)
        {
            Error(node, Diagnostics.Property_0_cannot_have_an_initializer_because_it_is_marked_abstract, Utilities.DeclarationNameToString(store, store.Name(n)));
        }
    }

    // checker.ts checkPropertySignature
    private void CheckPropertySignature(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(store.Name(node.Node)) == SyntaxKind.PrivateIdentifier)
        {
            Error(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }
        CheckPropertyDeclaration(node);
    }

    /// <summary>checker.ts checkVariableLikeDeclaration: variable, parameter, or property declarations.</summary>
    private void CheckVariableLikeDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        CheckDecorators(node);
        if (kind != SyntaxKind.BindingElement)
        {
            CheckSourceElement(At(node, store.Type(n)));
        }
        var name = store.Name(n);
        // JSDoc `function(string, string): string` syntax results in parameters with no name
        if (name.IsNone)
        {
            return;
        }
        // hasOnlyExpressionInitializer
        var initializer = kind is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.BindingElement or SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertyAssignment or SyntaxKind.EnumMember ? store.Initializer(n) : Node.None;
        // For a computed property, just check the initializer and exit
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName(At(node, name));
            if (!initializer.IsNone)
            {
                CheckExpressionCached(At(node, initializer));
            }
        }
        if (kind == SyntaxKind.BindingElement)
        {
            var propertyName = store.PropertyName(n);
            if (!propertyName.IsNone && store.Kind(name) == SyntaxKind.Identifier && Utilities.IsPartOfParameterDeclaration(store, n) &&
                Utilities.NodeIsMissing(store, store.Body(Utilities.GetContainingFunctionOrNone(store, n))))
            {
                // type F = ({a: string}) => void;
                //               ^^^^^^
                // variable renaming in function type notation is confusing,
                // so we forbid it even if noUnusedLocals is not enabled
                potentialUnusedRenamedBindingElementsInTypes.Add(node);
                return;
            }
            // (object rest prior to ES2018 needs the __rest helper: Gate C targets ES2025)
            // check computed properties inside property names of binding elements
            if (!propertyName.IsNone && store.Kind(propertyName) == SyntaxKind.ComputedPropertyName)
            {
                CheckComputedPropertyName(At(node, propertyName));
            }
            // check private/protected variable access
            var parent = At(node, store.Parent(store.Parent(n)));
            var parentCheckMode = !store.DotDotDotToken(n).IsNone ? CheckMode.RestBindingElement : CheckMode.Normal;
            var parentType = GetTypeForBindingElementParent(parent, parentCheckMode);
            var propName = !propertyName.IsNone ? propertyName : name;
            if (!parentType.IsNone && !Utilities.IsBindingPattern(store, propName))
            {
                var exprType = GetLiteralTypeFromPropertyName(At(node, propName));
                if (IsTypeUsableAsPropertyName(exprType))
                {
                    var nameText = GetPropertyNameFromType(exprType);
                    var property = GetPropertyOfType(parentType, nameText);
                    if (!property.IsNone)
                    {
                        MarkPropertyAsReferenced(property, NodeRef.None, isSelfTypeAccess: false); // A destructuring is never a write-only reference.
                        var parentInitializer = store.Initializer(parent.Node);
                        CheckPropertyAccessibility(node, !parentInitializer.IsNone && store.Kind(parentInitializer) == SyntaxKind.SuperKeyword, writing: false, parentType, property);
                    }
                }
            }
        }
        // For a binding pattern, check contained binding elements
        if (Utilities.IsBindingPattern(store, name))
        {
            CheckSourceElements(node, store.Elements(name));
        }
        // For a parameter declaration with an initializer, error and exit if the containing function doesn't have a body
        if (!store.Initializer(n).IsNone && Utilities.IsPartOfParameterDeclaration(store, n) &&
            Utilities.NodeIsMissing(store, store.Body(Utilities.GetContainingFunctionOrNone(store, n))))
        {
            Error(node, Diagnostics.A_parameter_initializer_is_only_allowed_in_a_function_or_constructor_implementation);
            return;
        }
        // For a binding pattern, validate the initializer and exit
        if (Utilities.IsBindingPattern(store, name))
        {
            if (IsInAmbientOrTypeNode(store, n))
            {
                return;
            }
            var needCheckInitializer = !initializer.IsNone && store.Kind(store.Parent(store.Parent(n))) != SyntaxKind.ForInStatement;
            var needCheckWidenedType = true;
            foreach (var element in store.Nodes(store.Elements(name)))
            {
                if (store.Kind(element) != SyntaxKind.OmittedExpression)
                {
                    needCheckWidenedType = false;
                    break;
                }
            }
            if (needCheckInitializer || needCheckWidenedType)
            {
                // Don't validate for-in initializer as it is already an error
                var widenedType = GetWidenedTypeForVariableLikeDeclaration(node, reportErrors: false);
                if (needCheckInitializer)
                {
                    var initializerType = CheckExpressionCached(At(node, initializer));
                    if (strictNullChecks && needCheckWidenedType)
                    {
                        CheckNonNullNonVoidType(initializerType, node);
                    }
                    else
                    {
                        CheckTypeAssignableToAndOptionallyElaborate(initializerType, GetWidenedTypeForVariableLikeDeclaration(node, reportErrors: false), node, At(node, initializer));
                    }
                }
                // check the binding pattern with empty elements
                if (needCheckWidenedType)
                {
                    if (store.Kind(name) == SyntaxKind.ArrayBindingPattern)
                    {
                        CheckIteratedTypeOrElementType(IterationUse.Destructuring, widenedType, undefinedType, node);
                    }
                    else if (strictNullChecks)
                    {
                        CheckNonNullNonVoidType(widenedType, node);
                    }
                }
            }
            return;
        }
        // For a commonjs `const x = require`, validate the alias and exit
        var symbol = GetSymbolOfDeclaration(node);
        if (symbol.IsNone)
        {
            return;
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0)
        {
            // (isVariableDeclarationInitializedToBareOrAccessedRequire: JavaScript, module C9)
            CheckAliasSymbol(node);
            return;
        }
        if (store.Kind(name) == SyntaxKind.BigIntLiteral)
        {
            Error(At(node, name), Diagnostics.A_bigint_literal_cannot_be_used_as_a_property_name);
        }
        if (kind == SyntaxKind.Parameter && (SymbolFlagsOf(symbol) & SymbolFlags.Property) != 0)
        {
            // the parameter symbol of a parameter property (getSymbolOfDeclaration returns the parameter's own symbol)
            var local = ResolveName(node, NameOf(symbol), NodeRef.None, SymbolFlags.Value, null, isUse: false);
            if (!local.IsNone)
            {
                symbol = local;
            }
        }
        var type = ConvertAutoToAny(GetTypeOfSymbol(symbol));
        if (node == ValueDeclarationOf(symbol))
        {
            // Node is the primary declaration of the symbol, just validate the initializer
            // Don't validate for-in initializer as it is already an error
            if (!initializer.IsNone)
            {
                var isJSObjectLiteralInitializer = Utilities.IsInJSFile(store, n) && store.Kind(initializer) == SyntaxKind.ObjectLiteralExpression &&
                    (store.Length(store.Properties(initializer)) == 0 || Utilities.IsPrototypeAccess(store, name)) && (ExportsOf(symbol)?.Count ?? 0) > 0;
                if (!isJSObjectLiteralInitializer && store.Kind(store.Parent(store.Parent(n))) != SyntaxKind.ForInStatement)
                {
                    var initializerRef = At(node, initializer);
                    var initializerType = CheckExpressionCached(initializerRef);
                    CheckTypeAssignableToAndOptionallyElaborate(initializerType, type, node, initializerRef);
                    var blockScopeKind = Utilities.GetCombinedNodeFlags(store, n) & NodeFlags.BlockScoped;
                    if (blockScopeKind == NodeFlags.AwaitUsing)
                    {
                        var globalAsyncDisposableType = GetGlobalAsyncDisposableType();
                        var globalDisposableType = GetGlobalDisposableType();
                        if (globalAsyncDisposableType != emptyObjectType && globalDisposableType != emptyObjectType)
                        {
                            var optionalDisposableType = GetUnionType([globalAsyncDisposableType, globalDisposableType, nullType, undefinedType]);
                            CheckTypeAssignableTo(WidenTypeForVariableLikeDeclaration(initializerType, node, reportErrors: false), optionalDisposableType, initializerRef,
                                Diagnostics.The_initializer_of_an_await_using_declaration_must_be_either_an_object_with_a_Symbol_asyncDispose_or_Symbol_dispose_method_or_be_null_or_undefined);
                        }
                    }
                    else if (blockScopeKind == NodeFlags.Using)
                    {
                        var globalDisposableType = GetGlobalDisposableType();
                        if (globalDisposableType != emptyObjectType)
                        {
                            var optionalDisposableType = GetUnionType([globalDisposableType, nullType, undefinedType]);
                            CheckTypeAssignableTo(WidenTypeForVariableLikeDeclaration(initializerType, node, reportErrors: false), optionalDisposableType, initializerRef,
                                Diagnostics.The_initializer_of_a_using_declaration_must_be_either_an_object_with_a_Symbol_dispose_method_or_be_null_or_undefined);
                        }
                    }
                }
            }
            if (DeclarationsOf(symbol) is { Count: > 1 } declarations)
            {
                if (declarations.Exists(d => d != node && Utilities.IsVariableLike(StoreOf(d), d.Node) && !AreDeclarationFlagsIdentical(d, node)))
                {
                    Error(At(node, name), Diagnostics.All_declarations_of_0_must_have_identical_modifiers, Utilities.DeclarationNameToString(store, name));
                }
            }
        }
        else
        {
            // Node is a secondary declaration, check that type is identical to primary declaration and check that
            // initializer is consistent with type associated with the node
            var declarationType = ConvertAutoToAny(GetWidenedTypeForVariableLikeDeclaration(node, reportErrors: false));
            if (!IsErrorType(type) && !IsErrorType(declarationType) && !IsTypeIdenticalTo(type, declarationType) && (SymbolFlagsOf(symbol) & SymbolFlags.Assignment) == 0)
            {
                ErrorNextVariableOrPropertyDeclarationMustHaveSameType(ValueDeclarationOf(symbol), type, node, declarationType);
            }
            if (!initializer.IsNone)
            {
                var initializerRef = At(node, initializer);
                CheckTypeAssignableToAndOptionallyElaborate(CheckExpressionCached(initializerRef), declarationType, node, initializerRef);
            }
            var valueDeclaration = ValueDeclarationOf(symbol);
            if (!valueDeclaration.IsNone && !AreDeclarationFlagsIdentical(node, valueDeclaration))
            {
                Error(At(node, name), Diagnostics.All_declarations_of_0_must_have_identical_modifiers, Utilities.DeclarationNameToString(store, name));
            }
        }
        if (kind != SyntaxKind.PropertyDeclaration && kind != SyntaxKind.PropertySignature)
        {
            // We know we don't have a binding pattern or computed name here
            CheckExportsOnMergedDeclarations(node);
            if (kind is SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement)
            {
                CheckVarDeclaredNamesNotShadowed(node);
            }
            CheckCollisionsForDeclarationName(node, At(node, name));
        }
    }

    // checker.ts areDeclarationFlagsIdentical
    private bool AreDeclarationFlagsIdentical(NodeRef left, NodeRef right)
    {
        var leftStore = StoreOf(left);
        var rightStore = StoreOf(right);
        if ((leftStore.Kind(left.Node) == SyntaxKind.Parameter && rightStore.Kind(right.Node) == SyntaxKind.VariableDeclaration) ||
            (leftStore.Kind(left.Node) == SyntaxKind.VariableDeclaration && rightStore.Kind(right.Node) == SyntaxKind.Parameter))
        {
            // Differences in optionality between parameters and variables are allowed.
            return true;
        }
        if (IsOptionalDeclaration(leftStore, left.Node) != IsOptionalDeclaration(rightStore, right.Node))
        {
            return false;
        }
        const ModifierFlags interestingFlags = ModifierFlags.Private | ModifierFlags.Protected | ModifierFlags.Async | ModifierFlags.Abstract |
            ModifierFlags.Readonly | ModifierFlags.Static;
        return GetSelectedEffectiveModifierFlags(left, interestingFlags) ==
            GetSelectedEffectiveModifierFlags(right, interestingFlags);
    }

    // checker.ts checkVarDeclaredNamesNotShadowed
    private void CheckVarDeclaredNamesNotShadowed(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // - ScriptBody : StatementList
        // It is a Syntax Error if any element of the LexicallyDeclaredNames of StatementList
        // also occurs in the VarDeclaredNames of StatementList.

        // - Block : { StatementList }
        // It is a Syntax Error if any element of the LexicallyDeclaredNames of StatementList
        // also occurs in the VarDeclaredNames of StatementList.

        // Variable declarations are hoisted to the top of their function scope. They can shadow
        // block scoped declarations, which bind tighter. this will not be flagged as duplicate definition
        // by the binder as the declaration scope is different.
        // A non-initialized declaration is a no-op as the block declaration will resolve before the var
        // declaration. the problem is if the declaration has an initializer. this will act as a write to the
        // block declared value. this is fine for let, but not const.
        // Only consider declarations with initializers, uninitialized const declarations will not
        // step on a let/const variable.
        // Do not consider const and const declarations, as duplicate block-scoped declarations
        // are handled by the binder.
        // We are only looking for const declarations that step on let\const declarations from a
        // different scope. e.g.:
        //      {
        //          const x = 0; // localDeclarationSymbol obtained after name resolution will correspond to this declaration
        //          const x = 0; // symbol for this declaration will be 'symbol'
        //      }

        // skip block-scoped variables and parameters
        if ((Utilities.GetCombinedNodeFlags(store, n) & NodeFlags.BlockScoped) != 0 || Utilities.IsPartOfParameterDeclaration(store, n))
        {
            return;
        }
        // NOTE: in ES6 spec initializer is required in variable declarations where name is binding pattern
        // so we'll always treat binding elements as initialized
        var symbol = GetSymbolOfDeclaration(node);
        if ((SymbolFlagsOf(symbol) & SymbolFlags.FunctionScopedVariable) != 0)
        {
            var name = store.Name(n);
            if (store.Kind(name) != SyntaxKind.Identifier)
            {
                return;
            }
            var localDeclarationSymbol = ResolveName(node, store.Texts.GetString(store.EscapedText(name))!, NodeRef.None, SymbolFlags.Variable, null, isUse: false);
            if (!localDeclarationSymbol.IsNone && localDeclarationSymbol != symbol && (SymbolFlagsOf(localDeclarationSymbol) & SymbolFlags.BlockScopedVariable) != 0)
            {
                if ((GetDeclarationNodeFlagsFromSymbol(localDeclarationSymbol) & NodeFlags.BlockScoped) != 0)
                {
                    var valueDeclaration = ValueDeclarationOf(localDeclarationSymbol);
                    var declarationStore = StoreOf(valueDeclaration);
                    var varDeclList = UtilitiesPublic.FindAncestor(declarationStore, valueDeclaration.Node, a => declarationStore.Kind(a) == SyntaxKind.VariableDeclarationList);
                    var container = !varDeclList.IsNone && declarationStore.Kind(declarationStore.Parent(varDeclList)) == SyntaxKind.VariableStatement && !declarationStore.Parent(declarationStore.Parent(varDeclList)).IsNone
                        ? declarationStore.Parent(declarationStore.Parent(varDeclList))
                        : Node.None;
                    // names of block-scoped and function scoped variables can collide only
                    // if block scoped variable is defined in the function\module\source file scope (because of variable hoisting)
                    var namesShareScope = !container.IsNone &&
                        ((declarationStore.Kind(container) == SyntaxKind.Block && Utilities.IsFunctionLike(declarationStore, declarationStore.Parent(container))) ||
                            declarationStore.Kind(container) is SyntaxKind.ModuleBlock or SyntaxKind.ModuleDeclaration or SyntaxKind.SourceFile);
                    // here we know that function scoped variable is "shadowed" by block scoped one
                    // a var declaration can't hoist past a lexical declaration and it results in a SyntaxError at runtime
                    if (!namesShareScope)
                    {
                        var nameText = SymbolToString(localDeclarationSymbol);
                        Error(node, Diagnostics.Cannot_initialize_outer_scoped_variable_0_in_the_same_scope_as_block_scoped_declaration_1, nameText, nameText);
                    }
                }
            }
        }
    }
}
