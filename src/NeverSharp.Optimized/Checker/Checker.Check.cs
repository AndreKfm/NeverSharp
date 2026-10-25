// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkSourceFile; of checkSourceElement the parts that resolve names and modules:
// checkIdentifier via checkExpression, checkTypeReferenceNode, checkImportDeclaration, checkImportEqualsDeclaration,
// checkExportDeclaration, checkExportSpecifier, checkExportAssignment, checkVariableLikeDeclaration's getTypeOfSymbol)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (checkSourceFile, checkSourceElement)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. TODO(C8): the checkSourceElement family proper (statements,
// declarations, classes and inheritance, grammar checks, unused-identifier suggestions, deferred nodes in TypeScript's
// order); C1 walks the file once in source order and performs the name, module and declared-type resolutions that
// report the core's diagnostics.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checkNodeDeferred: the deferred nodes of the file being checked, in deferral order (an insertion-ordered set)
    private readonly List<NodeRef> deferredNodes = [];
    private readonly HashSet<NodeRef> deferredNodeSet = [];

    /// <summary>checker.ts checkSourceFile (C1: the resolution walk, see the file header).</summary>
    private void CheckSourceFile(NodeRef file)
    {
        var store = StoreOf(file);
        // (skipLibCheck and isolated declaration files: Gate C checks everything)
        foreach (var statement in store.Nodes(store.Statements(file.Node)))
        {
            CheckNodeRecursive(At(file, statement));
        }
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
    }

    private void CheckNodeDeferred(NodeRef node)
    {
        if (deferredNodeSet.Add(node))
        {
            deferredNodes.Add(node);
        }
    }

    // checker.ts checkDeferredNode: function expression and object literal method bodies, object literal accessors,
    // class expression members
    private void CheckDeferredNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            {
                // checkFunctionExpressionOrObjectLiteralMethodDeferred
                var body = store.Body(n);
                if (!body.IsNone)
                {
                    if (store.Type(n).IsNone)
                    {
                        GetReturnTypeOfSignature(GetSignatureFromDeclaration(node));
                    }
                    CheckNodeRecursive(At(node, body));
                }
                break;
            }
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                CheckSignatureLikeDeclaration(node);
                break;
            case SyntaxKind.ClassExpression:
                // checkClassExpressionDeferred
                foreach (var member in store.Nodes(store.Members(n)))
                {
                    CheckNodeRecursive(At(node, member));
                }
                RegisterForUnusedIdentifiersCheck(node);
                break;
        }
    }

    // checkSourceElement's currentNode and instantiationCount (module C3)
    private void CheckNodeRecursive(NodeRef node)
    {
        var saveCurrentNode = currentNode;
        currentNode = node;
        instantiationCount = 0;
        CheckNodeRecursiveWorker(node);
        currentNode = saveCurrentNode;
    }

    private void CheckNodeRecursiveWorker(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        switch (kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.IndexSignature:
                CheckSignatureLikeDeclaration(node);
                return;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            {
                var parent = store.Parent(n);
                if (store.Kind(parent) == SyntaxKind.ObjectLiteralExpression)
                {
                    if (kind == SyntaxKind.MethodDeclaration)
                    {
                        // checkObjectLiteralMethod: the signature now, the body deferred
                        CheckSignatureLikeDeclaration(node, deferBody: true);
                    }
                    else
                    {
                        CheckNodeDeferred(node);
                    }
                    return;
                }
                CheckSignatureLikeDeclaration(node);
                return;
            }
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                // checkFunctionExpressionOrObjectLiteralMethod: the signature now, the body deferred
                CheckSignatureLikeDeclaration(node, deferBody: true);
                return;
            case SyntaxKind.ClassExpression:
                // checkClassExpression: checkClassLikeDeclaration now, the members deferred
                VisitChildren(node, except: store.Members(n));
                CheckMembersForOverrideModifier(node);
                CheckPropertyInitialization(node);
                CheckNodeDeferred(node);
                return;
        }
        var descend = CheckNodeWorker(node);
        if (descend)
        {
            VisitChildren(node, NodeList.None);
        }
        switch (kind)
        {
            case SyntaxKind.Block:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                if (LocalsOf(node) is not null)
                {
                    RegisterForUnusedIdentifiersCheck(node);
                }
                break;
            case SyntaxKind.SwitchStatement:
            {
                var caseBlock = At(node, store.CaseBlock(n));
                if (LocalsOf(caseBlock) is not null)
                {
                    RegisterForUnusedIdentifiersCheck(caseBlock);
                }
                break;
            }
            case SyntaxKind.ClassDeclaration:
                CheckMembersForOverrideModifier(node);
                CheckPropertyInitialization(node);
                RegisterForUnusedIdentifiersCheck(node);
                break;
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.InferType:
                RegisterForUnusedIdentifiersCheck(node);
                break;
            case SyntaxKind.TypeAliasDeclaration:
                if (store.Kind(store.Type(n)) != SyntaxKind.IntrinsicKeyword)
                {
                    RegisterForUnusedIdentifiersCheck(node);
                }
                break;
            case SyntaxKind.ModuleDeclaration:
                if (!store.Body(n).IsNone && !Utilities.IsGlobalScopeAugmentation(store, n))
                {
                    RegisterForUnusedIdentifiersCheck(node);
                }
                break;
        }
    }

    // checker.ts checkMembersForOverrideModifier / checkExistingMemberForOverrideModifier / checkMemberForOverrideModifier
    // (members with dynamic names are left to C8)
    private void CheckMembersForOverrideModifier(NodeRef node)
    {
        var store = StoreOf(node);
        var classSymbol = GetSymbolOfDeclaration(node);
        if (classSymbol.IsNone)
        {
            return;
        }
        var declaredType = GetDeclaredTypeOfSymbol(classSymbol);
        if ((Types.Flags(declaredType) & TypeFlags.Object) == 0)
        {
            return;
        }
        var type = GetTypeWithThisArgument(declaredType);
        var baseTypeNode = GetBaseTypeNodeOfClass(node);
        var baseTypes = baseTypeNode.IsNone ? [] : GetBaseTypes(declaredType);
        var baseWithThis = baseTypes.Count > 0 ? GetTypeWithThisArgument(baseTypes[0], Types.Object(declaredType).ThisType) : Type.None;
        var nodeInAmbientContext = (store.Flags(node.Node) & NodeFlags.Ambient) != 0;
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            if (Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Ambient))
            {
                continue;
            }
            if (store.Kind(member) == SyntaxKind.Constructor)
            {
                foreach (var parameter in store.Nodes(store.Parameters(member)))
                {
                    if (Utilities.IsParameterPropertyDeclaration(store, parameter, member))
                    {
                        CheckExistingMemberForOverrideModifier(node, type, classSymbol, baseWithThis, At(node, parameter), memberIsParameterProperty: true, nodeInAmbientContext);
                    }
                }
                continue;
            }
            if (store.Kind(member) is SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
            {
                CheckExistingMemberForOverrideModifier(node, type, classSymbol, baseWithThis, At(node, member), memberIsParameterProperty: false, nodeInAmbientContext);
            }
        }
    }

    private void CheckExistingMemberForOverrideModifier(NodeRef node, Type type, SymbolRef classSymbol, Type baseWithThis, NodeRef member, bool memberIsParameterProperty, bool nodeInAmbientContext)
    {
        var store = StoreOf(member);
        var nameNode = store.Name(member.Node);
        if (nameNode.IsNone || store.Kind(nameNode) == SyntaxKind.ComputedPropertyName)
        {
            return;
        }
        var declaredProp = GetSymbolOfDeclaration(member);
        if (declaredProp.IsNone)
        {
            return;
        }
        var memberHasOverrideModifier = Utilities.HasSyntacticModifierUncached(store, member.Node, ModifierFlags.Override);
        var memberHasAbstractModifier = Utilities.HasSyntacticModifierUncached(store, member.Node, ModifierFlags.Abstract);
        var memberIsStatic = !memberIsParameterProperty && Utilities.IsStatic(store, member.Node);
        if (!baseWithThis.IsNone && (memberHasOverrideModifier || compilerOptions.NoImplicitOverride == true))
        {
            var thisType = memberIsStatic ? GetTypeOfSymbol(classSymbol) : type;
            var baseType = memberIsStatic ? GetBaseConstructorTypeOfClass(GetDeclaredTypeOfSymbol(classSymbol)) : baseWithThis;
            var name = NameOf(declaredProp);
            var prop = GetPropertyOfType(thisType, name);
            var baseProp = (Types.Flags(baseType) & (TypeFlags.Object | TypeFlags.Intersection)) != 0 ? GetPropertyOfType(baseType, name) : SymbolRef.None;
            if ((Types.Flags(baseType) & TypeFlags.Any) != 0)
            {
                return; // (an any base: every member exists)
            }
            var baseClassName = TypeToString(baseWithThis);
            if (!prop.IsNone && baseProp.IsNone && memberHasOverrideModifier)
            {
                var suggestion = GetSpellingSuggestionForName(SymbolName(declaredProp), GetPropertiesOfType(baseType), SymbolFlags.ClassMember);
                if (!suggestion.IsNone)
                {
                    Error(member, Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0_Did_you_mean_1, baseClassName, SymbolToString(suggestion));
                }
                else
                {
                    Error(member, Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0, baseClassName);
                }
            }
            else if (!prop.IsNone && !baseProp.IsNone && DeclarationsOf(baseProp) is { } baseDeclarations && compilerOptions.NoImplicitOverride == true && !nodeInAmbientContext)
            {
                var baseHasAbstract = baseDeclarations.Exists(d => Utilities.HasSyntacticModifierUncached(StoreOf(d), d.Node, ModifierFlags.Abstract));
                if (memberHasOverrideModifier)
                {
                    return;
                }
                if (!baseHasAbstract)
                {
                    Error(member, memberIsParameterProperty
                        ? Diagnostics.This_parameter_property_must_have_an_override_modifier_because_it_overrides_a_member_in_base_class_0
                        : Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_a_member_in_the_base_class_0, baseClassName);
                }
                else if (memberHasAbstractModifier)
                {
                    Error(member, Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_an_abstract_method_that_is_declared_in_the_base_class_0, baseClassName);
                }
            }
        }
        else if (memberHasOverrideModifier)
        {
            Error(member, Diagnostics.This_member_cannot_have_an_override_modifier_because_its_containing_class_0_does_not_extend_another_class, TypeToString(GetDeclaredTypeOfSymbol(classSymbol)));
        }
    }

    // checker.ts checkPropertyInitialization (C1: classes without a constructor; isPropertyInitializedInConstructor needs
    // the flow analysis of C7, so properties of classes with a constructor are taken as initialized)
    private void CheckPropertyInitialization(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!strictNullChecks || !compilerOptions.GetStrictOptionValue(compilerOptions.StrictPropertyInitialization) || (store.Flags(n) & NodeFlags.Ambient) != 0)
        {
            return;
        }
        var members = store.Nodes(store.Members(n));
        foreach (var member in members)
        {
            if (store.Kind(member) == SyntaxKind.Constructor && !store.Body(member).IsNone)
            {
                return; // TODO(C7): isPropertyInitializedInConstructor
            }
        }
        foreach (var member in members)
        {
            if (store.Kind(member) != SyntaxKind.PropertyDeclaration || Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Ambient) ||
                Utilities.IsStatic(store, member) || Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Abstract) ||
                !store.ExclamationToken(member).IsNone || !store.Initializer(member).IsNone)
            {
                continue;
            }
            var propName = store.Name(member);
            if (store.Kind(propName) is not (SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier or SyntaxKind.ComputedPropertyName))
            {
                continue;
            }
            var type = GetTypeOfSymbol(GetSymbolOfDeclaration(At(node, member)));
            if ((Types.Flags(type) & (TypeFlags.AnyOrUnknown | TypeFlags.Undefined)) != 0 || IsUnsupportedType(type))
            {
                continue;
            }
            if ((Types.Flags(type) & TypeFlags.Union) != 0 && Array.Exists(Types.Elements(Types.Union(type).Types).ToArray(), t => (Types.Flags(t) & TypeFlags.Undefined) != 0))
            {
                continue;
            }
            Error(At(node, propName), Diagnostics.Property_0_has_no_initializer_and_is_not_definitely_assigned_in_the_constructor, Utilities.DeclarationNameToString(store, propName));
        }
    }

    private void VisitChildren(NodeRef node, NodeList except)
    {
        var store = StoreOf(node);
        var children = new List<Node>();
        NodeVisitor.ForEachChild<object>(store, node.Node, child =>
        {
            children.Add(child);
            return null;
        });
        var excluded = except.IsNone ? default : store.Nodes(except);
        foreach (var child in children)
        {
            if (!except.IsNone && excluded.Contains(child))
            {
                continue;
            }
            CheckNodeRecursive(At(node, child));
        }
    }

    // checkFunctionOrMethodDeclaration / checkSignatureDeclaration: decorators, type parameters, parameters and the
    // return type, then the registration for the unused check, then the body (deferred for function expressions)
    private void CheckSignatureLikeDeclaration(NodeRef node, bool deferBody = false)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var body = store.Body(n);
        var children = new List<Node>();
        NodeVisitor.ForEachChild<object>(store, n, child =>
        {
            children.Add(child);
            return null;
        });
        foreach (var child in children)
        {
            if (child != body)
            {
                CheckNodeRecursive(At(node, child));
            }
        }
        // checkSignatureDeclaration / checkFunctionOrMethodDeclaration: implicit any return types of bodyless
        // declarations (TODO(C8): the remaining signature declaration checks)
        if (store.Type(n).IsNone)
        {
            switch (store.Kind(n))
            {
                case SyntaxKind.ConstructSignature:
                    if (noImplicitAny)
                    {
                        Error(node, Diagnostics.Construct_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                    }
                    break;
                case SyntaxKind.CallSignature:
                    if (noImplicitAny)
                    {
                        Error(node, Diagnostics.Call_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                    }
                    break;
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    var isPrivate = Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Private) || (!store.Name(n).IsNone && store.Kind(store.Name(n)) == SyntaxKind.PrivateIdentifier);
                    var privateWithinAmbient = isPrivate && ((store.Flags(n) & NodeFlags.Ambient) != 0 || Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Ambient));
                    // object literal methods are checked as expressions (checkObjectLiteralMethod)
                    var isObjectLiteralMethod = store.Kind(store.Parent(n)) == SyntaxKind.ObjectLiteralExpression;
                    if (body.IsNone && !privateWithinAmbient && !isObjectLiteralMethod)
                    {
                        ReportImplicitAny(node, anyType);
                    }
                    break;
            }
        }
        if (store.Kind(n) != SyntaxKind.IndexSignature)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
        if (body.IsNone)
        {
            return;
        }
        if (deferBody)
        {
            CheckNodeDeferred(node);
        }
        else
        {
            CheckNodeRecursive(At(node, body));
        }
    }

    // Checks one node; returns whether its children are visited.
    private bool CheckNodeWorker(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        switch (kind)
        {
            case SyntaxKind.Identifier:
                CheckIdentifierReference(node);
                return false;
            case SyntaxKind.PropertyAccessExpression:
                if (Utilities.IsExpressionNode(store, n) && !Utilities.IsPartOfTypeNode(store, n))
                {
                    CheckExpressionCached(node);
                }
                // the name is resolved as a member, the expression as an expression
                CheckNodeRecursive(At(node, store.Expression(n)));
                return false;
            case SyntaxKind.QualifiedName:
                // resolved by the type reference or type query that contains it
                return false;
            case SyntaxKind.ThisKeyword:
                if (Utilities.IsExpressionNode(store, n) || Utilities.IsInTypeQuery(store, n))
                {
                    CheckThisExpression(node);
                }
                return false;
            case SyntaxKind.TypeReference:
                GetTypeFromTypeNode(node);
                // type arguments are type nodes of their own; the type name is resolved
                VisitList(node, store.TypeArguments(n));
                return false;
            case SyntaxKind.ExpressionWithTypeArguments:
                return CheckExpressionWithTypeArgumentsNode(node);
            case SyntaxKind.TypeQuery:
                GetTypeFromTypeQueryNode(node);
                VisitList(node, store.TypeArguments(n));
                return false;
            case SyntaxKind.ImportType:
                // checkImportType: the module reference is resolved in getTypeFromImportTypeNode (module C3)
                GetTypeFromTypeNode(node);
                CheckImportTypeModule(node);
                return false;
            case SyntaxKind.ImportDeclaration:
                CheckImportDeclaration(node);
                return false;
            case SyntaxKind.ImportEqualsDeclaration:
                CheckImportEqualsDeclaration(node);
                return false;
            case SyntaxKind.ExportDeclaration:
                CheckExportDeclaration(node);
                return false;
            case SyntaxKind.ExportAssignment:
                CheckExportAssignment(node);
                return false;
            case SyntaxKind.NamespaceExportDeclaration:
                return false;
            case SyntaxKind.ShorthandPropertyAssignment:
            {
                // checkShorthandPropertyAssignment: checkExpressionForMutableLocation(node.name)
                var name = At(node, store.Name(n));
                GetResolvedSymbol(name);
                var objectAssignmentInitializer = store.ObjectAssignmentInitializer(n);
                if (!objectAssignmentInitializer.IsNone)
                {
                    CheckNodeRecursive(At(node, objectAssignmentInitializer));
                }
                return false;
            }
            case SyntaxKind.Parameter:
                CheckGrammarParameterDecorators(node);
                CheckVariableLikeDeclarationType(node);
                return true;
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                CheckVariableLikeDeclarationType(node);
                return true;
            case SyntaxKind.LabeledStatement:
            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
            {
                // labels are not names; visit the statement of a labeled statement
                if (kind == SyntaxKind.LabeledStatement)
                {
                    CheckNodeRecursive(At(node, store.Statement(n)));
                }
                return false;
            }
            case SyntaxKind.MetaProperty:
                return false;
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
                CheckJsxNode(node);
                return false;
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxClosingElement:
                return false;
            case SyntaxKind.ModuleDeclaration:
                // the name of an ambient module is a string; the body is checked
                return true;
            default:
                return true;
        }
    }

    private void VisitList(NodeRef context, NodeList list)
    {
        if (list.IsNone)
        {
            return;
        }
        var store = StoreOf(context);
        foreach (var element in store.Nodes(list))
        {
            CheckNodeRecursive(At(context, element));
        }
    }

    // An identifier: an expression reference is resolved as a value (getResolvedSymbol reports cannot-find-name);
    // names of declarations, members and labels are not references.
    private void CheckIdentifierReference(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (Utilities.IsDeclarationName(store, n))
        {
            return;
        }
        var parent = store.Parent(n);
        var parentKind = store.Kind(parent);
        if (parentKind == SyntaxKind.PropertyAccessExpression && store.Name(parent) == n)
        {
            return;
        }
        if (parentKind is SyntaxKind.ImportSpecifier or SyntaxKind.ExportSpecifier or SyntaxKind.ImportClause or SyntaxKind.NamespaceImport
            or SyntaxKind.NamespaceExport or SyntaxKind.MetaProperty or SyntaxKind.TypePredicate or SyntaxKind.JsxAttribute)
        {
            return;
        }
        if (Utilities.IsExpressionNode(store, n))
        {
            if (Utilities.IsJsxTagName(store, n))
            {
                return; // TODO(C9)
            }
            CheckIdentifier(node);
        }
    }

    // checkVariableLikeDeclaration: the type of the declared symbol (reports implicit any and circularities)
    private void CheckVariableLikeDeclarationType(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (Utilities.IsBindingPattern(store, store.Name(n)))
        {
            // the implicit any errors of the pattern's elements (getTypeOfSymbol of a binding pattern parameter); TODO(C5):
            // destructuring checks
            if (store.Kind(n) == SyntaxKind.Parameter && store.Type(n).IsNone && store.Initializer(n).IsNone &&
                !(IsContextSensitiveFunctionOrObjectLiteralMethod(store, store.Parent(n)) && HasContextualType(At(node, store.Parent(n)))))
            {
                var patternSymbol = GetSymbolOfDeclaration(node);
                if (!patternSymbol.IsNone)
                {
                    GetTypeOfSymbol(patternSymbol);
                }
            }
            return;
        }
        var symbol = GetSymbolOfDeclaration(node);
        if (symbol.IsNone)
        {
            return;
        }
        if (store.Kind(n) == SyntaxKind.Parameter)
        {
            // the parameter symbol (a parameter property also declares a property)
            var parent = store.Parent(n);
            if (store.Kind(parent) is SyntaxKind.IndexSignature)
            {
                return;
            }
            if (IsContextSensitiveFunctionOrObjectLiteralMethod(store, parent) && HasContextualType(At(node, parent)) && store.Type(n).IsNone)
            {
                return; // contextually typed (C5)
            }
            var local = NodeSymbol(node);
            if (!local.IsNone && (SymbolFlagsOf(local) & SymbolFlags.Property) != 0)
            {
                local = ResolveName(node, NameOf(local), NodeRef.None, SymbolFlags.Value, null, isUse: false);
            }
            if (!local.IsNone)
            {
                GetTypeOfSymbol(local);
            }
            return;
        }
        GetTypeOfSymbol(symbol);
    }

    // ExpressionWithTypeArguments: in an interface's extends clause or a class's implements clause a type reference;
    // in a class's extends clause the expression is checked as a value; elsewhere an expression.
    private bool CheckExpressionWithTypeArgumentsNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (Utilities.IsPartOfTypeNode(store, n))
        {
            GetTypeFromTypeNode(node);
            VisitList(node, store.TypeArguments(n));
            return false;
        }
        if (Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(store, n))
        {
            var expression = At(node, store.Expression(n));
            CheckNodeRecursive(expression);
            VisitList(node, store.TypeArguments(n));
            return false;
        }
        return true;
    }

    // checker.ts checkImportDeclaration
    private void CheckImportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!IsModuleElementContext(store, n))
        {
            return; // (grammar error: checkGrammarModuleElementContext, C8)
        }
        if (CheckExternalImportOrExportDeclaration(node))
        {
            var importClause = store.ImportClause(n);
            var moduleSpecifier = At(node, store.ModuleSpecifier(n));
            if (!importClause.IsNone)
            {
                if (!store.Name(importClause).IsNone)
                {
                    CheckAliasSymbol(At(node, importClause));
                }
                var namedBindings = store.NamedBindings(importClause);
                if (!namedBindings.IsNone)
                {
                    if (store.Kind(namedBindings) == SyntaxKind.NamespaceImport)
                    {
                        CheckAliasSymbol(At(node, namedBindings));
                    }
                    else
                    {
                        var resolvedModule = ResolveExternalModuleName(node, moduleSpecifier);
                        if (!resolvedModule.IsNone)
                        {
                            foreach (var element in store.Nodes(store.Elements(namedBindings)))
                            {
                                CheckAliasSymbol(At(node, element));
                            }
                        }
                    }
                }
            }
            else if (noUncheckedSideEffectImports)
            {
                ResolveExternalModuleName(node, moduleSpecifier, ignoreErrors: false, Diagnostics.Cannot_find_module_or_type_declarations_for_side_effect_import_of_0);
            }
        }
        // (checkImportAttributes: C8)
    }

    // checker.ts checkGrammarModuleElementContext (the context test)
    private static bool IsModuleElementContext(NodeStore store, Node node) =>
        store.Kind(store.Parent(node)) is SyntaxKind.SourceFile or SyntaxKind.ModuleBlock or SyntaxKind.ModuleDeclaration;

    // checker.ts checkExternalImportOrExportDeclaration
    private bool CheckExternalImportOrExportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var moduleName = Utilities.GetExternalModuleName(store, n);
        if (moduleName.IsNone || Utilities.NodeIsMissing(store, moduleName))
        {
            // Should be a parse error.
            return false;
        }
        if (store.Kind(moduleName) != SyntaxKind.StringLiteral)
        {
            Error(At(node, moduleName), Diagnostics.String_literal_expected);
            return false;
        }
        var parent = store.Parent(n);
        var inAmbientExternalModule = store.Kind(parent) == SyntaxKind.ModuleBlock && Utilities.IsAmbientModule(store, store.Parent(parent));
        if (store.Kind(parent) != SyntaxKind.SourceFile && !inAmbientExternalModule)
        {
            Error(At(node, moduleName), store.Kind(n) == SyntaxKind.ExportDeclaration
                ? Diagnostics.Export_declarations_are_not_permitted_in_a_namespace
                : Diagnostics.Import_declarations_in_a_namespace_cannot_reference_a_module);
            return false;
        }
        if (inAmbientExternalModule && Utilities.IsExternalModuleNameRelative(store.Texts.GetString(store.Text(moduleName))!))
        {
            // (isTopLevelInExternalModuleAugmentation: module augmentations of files, which Gate C's programs do not resolve)
            if (!Utilities.IsModuleAugmentationExternal(store, store.Parent(parent)) || !IsTopLevelInExternalModuleAugmentation(store, n))
            {
                Error(node, Diagnostics.Import_or_export_declaration_in_an_ambient_module_declaration_cannot_reference_module_through_relative_module_name);
                return false;
            }
        }
        if (store.Kind(n) != SyntaxKind.ImportEqualsDeclaration)
        {
            var attributes = store.Attributes(n);
            if (!attributes.IsNone)
            {
                var diagnostic = store.Token(attributes) == SyntaxKind.WithKeyword ? Diagnostics.Import_attribute_values_must_be_string_literal_expressions : Diagnostics.Import_assertion_values_must_be_string_literal_expressions;
                var hasError = false;
                foreach (var attr in store.Nodes(store.Elements(attributes)))
                {
                    var value = store.Value(attr);
                    if (store.Kind(value) != SyntaxKind.StringLiteral)
                    {
                        hasError = true;
                        Error(At(node, value), diagnostic);
                    }
                }
                return !hasError;
            }
        }
        return true;
    }

    private static bool IsTopLevelInExternalModuleAugmentation(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return !parent.IsNone && store.Kind(parent) == SyntaxKind.ModuleBlock && Utilities.IsModuleAugmentationExternal(store, store.Parent(parent));
    }

    // checker.ts checkAliasSymbol: resolves the alias (reports the module and member errors of its target)
    private void CheckAliasSymbol(NodeRef node)
    {
        var symbol = GetSymbolOfDeclaration(node);
        if (symbol.IsNone)
        {
            return;
        }
        ResolveAlias(symbol);
        // (the conflicts of the alias with local declarations and the type-only checks: C8)
    }

    // checker.ts checkImportEqualsDeclaration
    private void CheckImportEqualsDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!IsModuleElementContext(store, n))
        {
            return;
        }
        if (Utilities.IsInternalModuleImportEqualsDeclaration(store, n) || CheckExternalImportOrExportDeclaration(node))
        {
            CheckAliasSymbol(node);
            if (store.Kind(store.ModuleReference(n)) != SyntaxKind.ExternalModuleReference)
            {
                var target = ResolveAlias(GetSymbolOfDeclaration(node));
                if (target != unknownSymbol)
                {
                    var targetFlags = GetSymbolFlags(target);
                    if ((targetFlags & SymbolFlags.Value) != 0)
                    {
                        // Target is a value symbol, check that it is not hidden by a local declaration with the same name
                        var moduleName = At(node, Utilities.GetFirstIdentifier(store, store.ModuleReference(n)));
                        var resolved = ResolveEntityName(moduleName, SymbolFlags.Value | SymbolFlags.Namespace);
                        if (!resolved.IsNone && (SymbolFlagsOf(resolved) & SymbolFlags.Namespace) == 0)
                        {
                            Error(moduleName, Diagnostics.Module_0_is_hidden_by_a_local_declaration_with_the_same_name, Utilities.DeclarationNameToString(store, moduleName.Node));
                        }
                    }
                }
            }
            else if (moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext && !store.IsTypeOnly(n) && (store.Flags(n) & NodeFlags.Ambient) == 0)
            {
                // Import equals declaration cannot be emitted as ESM
                GrammarErrorOnNode(node, Diagnostics.Import_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_import_Asterisk_as_ns_from_mod_import_a_from_mod_import_d_from_mod_or_another_module_format_instead);
            }
        }
    }

    // checker.ts grammarErrorOnNode: reported only when the file has no parse errors
    private void GrammarErrorOnNode(NodeRef node, DiagnosticMessage message, params object?[] args)
    {
        var file = FileAt(node.File);
        if (file.ParseDiagnostics.Count == 0)
        {
            diagnostics.Add(CreateDiagnosticForNode(node, message, args));
        }
    }

    // checker.ts grammarErrorOnFirstToken: reported only when the file has no parse errors
    private void GrammarErrorOnFirstToken(NodeRef node, DiagnosticMessage message, params object?[] args)
    {
        var file = FileAt(node.File);
        if (file.ParseDiagnostics.Count == 0)
        {
            var span = Utilities.GetSpanOfTokenAtPosition(file.Store, file.Root, file.Store.Pos(node.Node));
            diagnostics.Add(ProgramDiagnostic.FromFile(file, Utilities.CreateFileDiagnostic(file.Store, file.Root, span.Start, span.Length, message, args)));
        }
    }

    // checkGrammarModifiers, the decorator part for parameters (TODO(C8): the remaining modifier grammar): without
    // experimentalDecorators, nodeCanBeDecorated is false for parameters
    private void CheckGrammarParameterDecorators(NodeRef parameter)
    {
        var store = StoreOf(parameter);
        var modifiers = store.Modifiers(parameter.Node);
        if (modifiers.IsNone || compilerOptions.ExperimentalDecorators == true)
        {
            return;
        }
        var first = store.Nodes(modifiers);
        if (first.Length == 0 || store.Kind(first[0]) != SyntaxKind.Decorator)
        {
            return;
        }
        var name = store.Name(parameter.Node);
        if (store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "this"))
        {
            return; // (Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters: C8)
        }
        GrammarErrorOnFirstToken(parameter, Diagnostics.Decorators_are_not_valid_here);
    }

    // checker.ts checkExportDeclaration
    private void CheckExportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!IsModuleElementContext(store, n))
        {
            return;
        }
        var moduleSpecifier = store.ModuleSpecifier(n);
        if (moduleSpecifier.IsNone || CheckExternalImportOrExportDeclaration(node))
        {
            var exportClause = store.ExportClause(n);
            if (!exportClause.IsNone && store.Kind(exportClause) != SyntaxKind.NamespaceExport)
            {
                // export { x, y }
                // export { x, y } from "foo"
                foreach (var specifier in store.Nodes(store.Elements(exportClause)))
                {
                    CheckExportSpecifier(At(node, specifier));
                }
                var parent = store.Parent(n);
                var inAmbientExternalModule = store.Kind(parent) == SyntaxKind.ModuleBlock && Utilities.IsAmbientModule(store, store.Parent(parent));
                var inAmbientNamespaceDeclaration = !inAmbientExternalModule && store.Kind(parent) == SyntaxKind.ModuleBlock && moduleSpecifier.IsNone && (store.Flags(n) & NodeFlags.Ambient) != 0;
                if (store.Kind(parent) != SyntaxKind.SourceFile && !inAmbientExternalModule && !inAmbientNamespaceDeclaration)
                {
                    Error(node, Diagnostics.Export_declarations_are_not_permitted_in_a_namespace);
                }
            }
            else
            {
                // export * from "foo"
                // export * as ns from "foo";
                var moduleSymbol = ResolveExternalModuleName(node, At(node, moduleSpecifier));
                if (!moduleSymbol.IsNone && (ExportsOf(moduleSymbol)?.Has(InternalSymbolName.ExportEquals) ?? false))
                {
                    Error(At(node, moduleSpecifier), Diagnostics.Module_0_uses_export_and_cannot_be_used_with_export_Asterisk, SymbolToString(moduleSymbol));
                }
                else if (!exportClause.IsNone)
                {
                    CheckAliasSymbol(At(node, exportClause));
                }
            }
        }
    }

    // checker.ts checkExportSpecifier
    private void CheckExportSpecifier(NodeRef node)
    {
        CheckAliasSymbol(node);
        var store = StoreOf(node);
        var n = node.Node;
        var hasModuleSpecifier = !store.ModuleSpecifier(store.Parent(store.Parent(n))).IsNone;
        if (!hasModuleSpecifier)
        {
            var exportedName = !store.PropertyName(n).IsNone ? store.PropertyName(n) : store.Name(n);
            if (store.Kind(exportedName) == SyntaxKind.StringLiteral)
            {
                return; // Skip for invalid syntax like this: export { "x" }
            }
            // find immediate value referenced by exported name (SymbolFlags.Alias is set so we don't chase down aliases)
            var symbol = ResolveName(At(node, exportedName), store.Texts.GetString(store.EscapedText(exportedName))!, NodeRef.None,
                SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, null, isUse: true);
            if (!symbol.IsNone && (symbol == undefinedSymbol || symbol == globalThisSymbol ||
                (DeclarationsOf(symbol) is { Count: > 0 } declarations && IsGlobalSourceFile(GetDeclarationContainer(declarations[0])))))
            {
                Error(At(node, exportedName), Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, store.Texts.GetString(Utilities.IdText(store, exportedName)));
            }
        }
    }

    // utilities.ts getDeclarationContainer
    private NodeRef GetDeclarationContainer(NodeRef node)
    {
        var store = StoreOf(node);
        var n = Utilities.GetRootDeclaration(store, node.Node);
        for (n = store.Parent(n); !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) is not (SyntaxKind.VariableDeclaration or SyntaxKind.VariableDeclarationList or SyntaxKind.ImportSpecifier
                or SyntaxKind.NamedImports or SyntaxKind.NamespaceImport or SyntaxKind.ImportClause))
            {
                return At(node, n);
            }
        }
        return NodeRef.None;
    }

    // checker.ts checkExportAssignment (the name resolution part)
    private void CheckExportAssignment(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!IsModuleElementContext(store, n))
        {
            return;
        }
        var parent = store.Parent(n);
        var container = store.Kind(parent) == SyntaxKind.SourceFile ? parent : store.Parent(parent);
        if (store.Kind(container) == SyntaxKind.ModuleDeclaration && !Utilities.IsAmbientModule(store, container))
        {
            Error(node, store.IsExportEquals(n) ? Diagnostics.An_export_assignment_cannot_be_used_in_a_namespace : Diagnostics.A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
            return;
        }
        var expression = At(node, store.Expression(n));
        if (store.Kind(expression.Node) == SyntaxKind.Identifier)
        {
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(expression, SymbolFlags.All, ignoreErrors: true, dontResolveAlias: true, node));
            if (!sym.IsNone)
            {
                // If not a value, we're interpreting the identifier as a type export, along the lines of (`export { Id as default }`)
                if ((GetSymbolFlags(sym) & SymbolFlags.Value) != 0)
                {
                    // However if it is a value, we need to check it's being used correctly
                    CheckExpressionCached(expression);
                }
            }
            else
            {
                CheckExpressionCached(expression); // doesn't resolve, check as expression to mark as error
            }
        }
        else
        {
            CheckNodeRecursive(expression);
        }
        // (checkExternalModuleExports: C8)
        if ((store.Flags(n) & NodeFlags.Ambient) != 0 && !Utilities.IsEntityNameExpression(store, expression.Node))
        {
            GrammarErrorOnNode(expression, Diagnostics.The_expression_of_an_export_assignment_must_be_an_identifier_or_qualified_name_in_an_ambient_context);
        }
        if (store.IsExportEquals(n))
        {
            // Forbid export= in esm implementation files, and esm mode declaration files
            var format = GetImpliedNodeFormatForEmit(node.File);
            if (moduleKind >= ModuleKind.ES2015 && moduleKind != ModuleKind.Preserve &&
                (((store.Flags(n) & NodeFlags.Ambient) != 0 && format == ModuleKind.ESNext) ||
                    ((store.Flags(n) & NodeFlags.Ambient) == 0 && format != ModuleKind.CommonJS)))
            {
                // export assignment is not supported in es6 modules
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_export_default_or_another_module_format_instead);
            }
            else if (moduleKind == ModuleKind.System && (store.Flags(n) & NodeFlags.Ambient) == 0)
            {
                // system modules does not support export assignment
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_is_not_supported_when_module_flag_is_system);
            }
        }
    }

    // program.ts getImpliedNodeFormatForEmit for Gate C's options (module ES2022, moduleResolution bundler, no
    // package.json scopes): CommonJS for .cts/.cjs files, ESNext for .mts/.mjs files, otherwise undefined (None).
    private ModuleKind? GetImpliedNodeFormatForEmit(int file)
    {
        var fileName = FileNameOf(file);
        if (fileName.EndsWith(".cts", StringComparison.Ordinal) || fileName.EndsWith(".cjs", StringComparison.Ordinal))
        {
            return ModuleKind.CommonJS;
        }
        if (fileName.EndsWith(".mts", StringComparison.Ordinal) || fileName.EndsWith(".mjs", StringComparison.Ordinal))
        {
            return ModuleKind.ESNext;
        }
        return null;
    }

    // import("x") in type positions: the module reference
    private void CheckImportTypeModule(NodeRef node)
    {
        var store = StoreOf(node);
        var argument = store.Argument(node.Node);
        if (store.Kind(argument) == SyntaxKind.LiteralType && Utilities.IsStringLiteralLike(store, store.Literal(argument)))
        {
            ResolveExternalModuleName(node, At(node, store.Literal(argument)));
        }
        VisitList(node, store.TypeArguments(node.Node));
    }
}
