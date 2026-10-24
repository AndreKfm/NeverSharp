// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/binder.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/binder/binder.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C# (module B3): the delayed binding of JSDoc typedef/callback/enum tags, the
// JavaScript special assignments (CommonJS exports and module.exports, this/prototype/expando property assignments,
// Object.defineProperty) and CommonJS module detection; forEachIdentifierInEntityName's callbacks are a mode argument.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Binder
{
    // The callbacks binder.ts passes to forEachIdentifierInEntityName.
    private enum EntityNameAction
    {
        // bindObjectDefinePropertyExport / bindExportsPropertyAssignment: add a Module | Assignment declaration to the
        // symbol if there is one.
        AddModuleDeclaration,

        // bindPotentiallyMissingNamespaces: make symbols or add declarations for intermediate containers.
        DeclareNamespace,
    }

    private TextRef nameModule;

    private void DelayedBindJSDocTypedefTag()
    {
        if (delayedTypeAliases is null)
        {
            return;
        }
        var saveContainer = container;
        var saveLastContainer = lastContainer;
        var saveBlockScopeContainer = blockScopeContainer;
        var saveParent = parent;
        var saveCurrentFlow = currentFlow;
        foreach (var typeAlias in delayedTypeAliases)
        {
            var host = store.Parent(store.Parent(typeAlias));
            var enclosingContainer = Utilities.GetEnclosingContainer(store, host);
            container = !enclosingContainer.IsNone ? enclosingContainer : file;
            var enclosingBlockScopeContainer = Utilities.GetEnclosingBlockScopeContainer(store, host);
            blockScopeContainer = !enclosingBlockScopeContainer.IsNone ? enclosingBlockScopeContainer : file;
            currentFlow = b.CreateFlowNode(FlowFlags.Start, Node.None, FlowNode.None);
            parent = typeAlias;
            Bind(store.TypeExpression(typeAlias));
            var declName = Utilities.GetNameOfDeclaration(store, typeAlias);
            var isEnumTag = store.Kind(typeAlias) == SyntaxKind.JSDocEnumTag;
            var fullName = isEnumTag ? Node.None : store.FullName(typeAlias);
            if ((isEnumTag || fullName.IsNone) && !declName.IsNone && Utilities.IsPropertyAccessEntityNameExpression(store, store.Parent(declName)))
            {
                // typedef anchored to an A.B.C assignment - we need to bind into B's namespace under name C
                var declNameParent = store.Parent(declName);
                var isTopLevel = IsTopLevelNamespaceAssignment(declNameParent);
                if (isTopLevel)
                {
                    BindPotentiallyMissingNamespaces(b.SymbolOf(file), declNameParent, isTopLevel, HasPrototypeAncestor(declName), /*containerIsClass*/ false);
                    var oldContainer = container;
                    switch (Utilities.GetAssignmentDeclarationPropertyAccessKind(store, declNameParent))
                    {
                        case AssignmentDeclarationKind.ExportsProperty:
                        case AssignmentDeclarationKind.ModuleExports:
                            container = !IsExternalOrCommonJsModule(file) ? Node.None : file;
                            break;
                        case AssignmentDeclarationKind.ThisProperty:
                            container = store.Expression(declNameParent);
                            break;
                        case AssignmentDeclarationKind.PrototypeProperty:
                            container = store.Name(store.Expression(declNameParent));
                            break;
                        case AssignmentDeclarationKind.Property:
                            var expression = store.Expression(declNameParent);
                            container = IsExportsOrModuleExportsOrAlias(expression) ? file
                                : store.Kind(expression) == SyntaxKind.PropertyAccessExpression ? store.Name(expression)
                                : expression;
                            break;
                        case AssignmentDeclarationKind.None:
                            Debug.Fail("Shouldn't have detected typedef or enum on non-assignment declaration");
                            break;
                    }
                    if (!container.IsNone)
                    {
                        DeclareModuleMember(typeAlias, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                    }
                    container = oldContainer;
                }
            }
            else if (isEnumTag || fullName.IsNone || store.Kind(fullName) == SyntaxKind.Identifier)
            {
                parent = store.Parent(typeAlias);
                BindBlockScopedDeclaration(typeAlias, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
            }
            else
            {
                Bind(fullName);
            }
        }
        container = saveContainer;
        lastContainer = saveLastContainer;
        blockScopeContainer = saveBlockScopeContainer;
        parent = saveParent;
        currentFlow = saveCurrentFlow;
    }

    // findAncestor(declName, d => isPropertyAccessExpression(d) && d.name.escapedText === "prototype").
    private bool HasPrototypeAncestor(Node node)
    {
        for (var d = node; !d.IsNone; d = store.Parent(d))
        {
            if (store.Kind(d) == SyntaxKind.PropertyAccessExpression && texts.Equals(store.EscapedText(store.Name(d)), "prototype"))
            {
                return true;
            }
        }
        return false;
    }

    private bool SetCommonJsModuleIndicator(Node node)
    {
        var externalModuleIndicator = store.ExternalModuleIndicator(file);
        if (externalModuleIndicator is not null && externalModuleIndicator is not true)
        {
            return false;
        }
        if (commonJsModuleIndicator.IsNone)
        {
            commonJsModuleIndicator = node;
            b.CommonJsModuleIndicator = node;
            if (externalModuleIndicator is null)
            {
                BindSourceFileAsExternalModule();
            }
        }
        return true;
    }

    private void BindObjectDefinePropertyExport(Node node)
    {
        if (!SetCommonJsModuleIndicator(node))
        {
            return;
        }
        var symbol = ForEachIdentifierInEntityName(store.Element(store.Arguments(node), 0), Symbol.None, EntityNameAction.AddModuleDeclaration);
        if (!symbol.IsNone)
        {
            var flags = SymbolFlags.Property | SymbolFlags.ExportValue;
            DeclareSymbol(b.Exports(symbol), symbol, node, flags, SymbolFlags.None);
        }
    }

    private void BindExportsPropertyAssignment(Node node)
    {
        // When we create a property via 'exports.foo = bar', the 'exports.foo' property access
        // expression is the declaration
        if (!SetCommonJsModuleIndicator(node))
        {
            return;
        }
        var left = store.Left(node);
        var leftExpression = store.Expression(left);
        var symbol = ForEachIdentifierInEntityName(leftExpression, Symbol.None, EntityNameAction.AddModuleDeclaration);
        if (!symbol.IsNone)
        {
            var isAlias = Utilities.IsAliasableExpression(store, store.Right(node)) && (Utilities.IsExportsIdentifier(store, leftExpression) || Utilities.IsModuleExportsAccessExpression(store, leftExpression));
            var flags = isAlias ? SymbolFlags.Alias : SymbolFlags.Property | SymbolFlags.ExportValue;
            Utilities.SetParent(store, left, node);
            DeclareSymbol(b.Exports(symbol), symbol, left, flags, SymbolFlags.None);
        }
    }

    private void BindModuleExportsAssignment(Node node)
    {
        // A common practice in node modules is to set 'export = module.exports = {}', this ensures that 'exports'
        // is still pointing to 'module.exports'.
        // We do not want to consider this as 'export=' since a module can have only one of these.
        // Similarly we do not want to treat 'module.exports = exports' as an 'export='.
        if (!SetCommonJsModuleIndicator(node))
        {
            return;
        }
        var assignedExpression = Utilities.GetRightMostAssignedExpression(store, store.Right(node));
        if (Utilities.IsEmptyObjectLiteral(store, assignedExpression) || container == file && IsExportsOrModuleExportsOrAlias(assignedExpression))
        {
            return;
        }

        var fileSymbol = b.SymbolOf(file);
        if (store.Kind(assignedExpression) == SyntaxKind.ObjectLiteralExpression && AllShorthandPropertyAssignments(store.Properties(assignedExpression)))
        {
            foreach (var property in store.Nodes(store.Properties(assignedExpression)))
            {
                // bindExportAssignedObjectMemberAlias
                DeclareSymbol(b.Exports(fileSymbol), fileSymbol, property, SymbolFlags.Alias | SymbolFlags.Assignment, SymbolFlags.None);
            }
            return;
        }

        // 'module.exports = expr' assignment
        var flags = Utilities.ExportAssignmentIsAlias(store, node)
            ? SymbolFlags.Alias // An export= with an EntityNameExpression or a ClassExpression exports all meanings of that identifier or class
            : SymbolFlags.Property | SymbolFlags.ExportValue | SymbolFlags.ValueModule;
        var symbol = DeclareSymbol(b.Exports(fileSymbol), fileSymbol, node, flags | SymbolFlags.Assignment, SymbolFlags.None);
        SetValueDeclaration(symbol, node);
    }

    private bool AllShorthandPropertyAssignments(NodeList properties)
    {
        foreach (var property in store.Nodes(properties))
        {
            if (store.Kind(property) != SyntaxKind.ShorthandPropertyAssignment)
            {
                return false;
            }
        }
        return true;
    }

    private void BindThisPropertyAssignment(Node node)
    {
        Debug.Assert(Utilities.IsInJSFile(store, node));
        // private identifiers *must* be declared (even in JS files)
        var hasPrivateIdentifier = (store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.Left(node)) == SyntaxKind.PropertyAccessExpression && store.Kind(store.Name(store.Left(node))) == SyntaxKind.PrivateIdentifier)
            || (store.Kind(node) == SyntaxKind.PropertyAccessExpression && store.Kind(store.Name(node)) == SyntaxKind.PrivateIdentifier);
        if (hasPrivateIdentifier)
        {
            return;
        }
        var thisContainer = GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        switch (store.Kind(thisContainer))
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
                var constructorSymbol = b.SymbolOf(thisContainer);
                // For `f.prototype.m = function() { this.x = 0; }`, `this.x = 0` should modify `f`'s members, not the function expression.
                var thisContainerParent = store.Parent(thisContainer);
                if (store.Kind(thisContainerParent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(thisContainerParent)) == SyntaxKind.EqualsToken)
                {
                    var l = store.Left(thisContainerParent);
                    if (Utilities.IsBindableStaticAccessExpression(store, l) && Utilities.IsPrototypeAccess(store, store.Expression(l)))
                    {
                        constructorSymbol = LookupSymbolForPropertyAccess(store.Expression(store.Expression(l)), thisParentContainer);
                    }
                }

                if (!constructorSymbol.IsNone && !b.ValueDeclaration(constructorSymbol).IsNone)
                {
                    // Declare a 'member' if the container is an ES5 class or ES6 constructor
                    if (b.Members(constructorSymbol).IsNone)
                    {
                        b.SetMembers(constructorSymbol, b.CreateTable());
                    }
                    // It's acceptable for multiple 'this' assignments of the same identifier to occur
                    if (Utilities.HasDynamicName(store, node))
                    {
                        BindDynamicallyNamedThisPropertyAssignment(node, constructorSymbol, b.Members(constructorSymbol));
                    }
                    else
                    {
                        DeclareSymbol(b.Members(constructorSymbol), constructorSymbol, node, SymbolFlags.Property | SymbolFlags.Assignment, SymbolFlags.PropertyExcludes & ~SymbolFlags.Property);
                    }
                    AddDeclarationToSymbol(constructorSymbol, b.ValueDeclaration(constructorSymbol), SymbolFlags.Class);
                }
                break;

            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ClassStaticBlockDeclaration:
                // this.foo assignment in a JavaScript class
                // Bind this property to the containing class
                var containingClass = store.Parent(thisContainer);
                var classSymbol = b.SymbolOf(containingClass);
                var symbolTable = Utilities.IsStatic(store, thisContainer) ? b.Exports(classSymbol) : b.Members(classSymbol);
                if (Utilities.HasDynamicName(store, node))
                {
                    BindDynamicallyNamedThisPropertyAssignment(node, classSymbol, symbolTable);
                }
                else
                {
                    DeclareSymbol(symbolTable, classSymbol, node, SymbolFlags.Property | SymbolFlags.Assignment, SymbolFlags.None, /*isReplaceableByMethod*/ true);
                }
                break;
            case SyntaxKind.SourceFile:
                // this.property = assignment in a source file -- declare symbol in exports for a module, in locals for a script
                if (Utilities.HasDynamicName(store, node))
                {
                    break;
                }
                else if (thisContainer == file && !commonJsModuleIndicator.IsNone)
                {
                    var fileSymbol = b.SymbolOf(thisContainer);
                    DeclareSymbol(b.Exports(fileSymbol), fileSymbol, node, SymbolFlags.Property | SymbolFlags.ExportValue, SymbolFlags.None);
                }
                else
                {
                    DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.FunctionScopedVariableExcludes);
                }
                break;
            // Namespaces are not allowed in javascript files, so do nothing here
            case SyntaxKind.ModuleDeclaration:
                break;
            default:
                Debug.Fail("Unexpected node kind " + store.Kind(thisContainer));
                break;
        }
    }

    private void BindDynamicallyNamedThisPropertyAssignment(Node node, Symbol symbol, SymbolTable symbolTable)
    {
        DeclareSymbol(symbolTable, symbol, node, SymbolFlags.Property, SymbolFlags.None, /*isReplaceableByMethod*/ true, /*isComputedName*/ true);
        AddLateBoundAssignmentDeclarationToSymbol(node, symbol);
    }

    private void AddLateBoundAssignmentDeclarationToSymbol(Node node, Symbol symbol)
    {
        if (!symbol.IsNone)
        {
            // (a Map keyed by node id: a node already there keeps its place)
            foreach (var member in b.AssignmentDeclarationMembers(symbol))
            {
                if (member == node)
                {
                    return;
                }
            }
            b.AddAssignmentDeclarationMember(symbol, node);
        }
    }

    private void BindSpecialPropertyDeclaration(Node node)
    {
        if (store.Kind(store.Expression(node)) == SyntaxKind.ThisKeyword)
        {
            BindThisPropertyAssignment(node);
        }
        else if (Utilities.IsBindableStaticAccessExpression(store, node) && store.Kind(store.Parent(store.Parent(node))) == SyntaxKind.SourceFile)
        {
            if (Utilities.IsPrototypeAccess(store, store.Expression(node)))
            {
                BindPrototypePropertyAssignment(node, store.Parent(node));
            }
            else
            {
                BindStaticPropertyAssignment(node);
            }
        }
    }

    /// <summary>For `x.prototype = { p, ... }`, declare members p,... if `x` is function/class/{}, or not declared.</summary>
    private void BindPrototypeAssignment(Node node)
    {
        Utilities.SetParent(store, store.Left(node), node);
        Utilities.SetParent(store, store.Right(node), node);
        BindPropertyAssignment(store.Expression(store.Left(node)), store.Left(node), /*isPrototypeProperty*/ false, /*containerIsClass*/ true);
    }

    private void BindObjectDefinePrototypeProperty(Node node)
    {
        var namespaceSymbol = LookupSymbolForPropertyAccess(store.Expression(store.Element(store.Arguments(node), 0)), container);
        if (!namespaceSymbol.IsNone && !b.ValueDeclaration(namespaceSymbol).IsNone)
        {
            // Ensure the namespace symbol becomes class-like
            AddDeclarationToSymbol(namespaceSymbol, b.ValueDeclaration(namespaceSymbol), SymbolFlags.Class);
        }
        BindPotentiallyNewExpandoMemberToNamespace(node, namespaceSymbol, /*isPrototypeProperty*/ true);
    }

    /// <summary>
    /// For `x.prototype.y = z`, declare a member `y` on `x` if `x` is a function or class, or not declared.
    /// Note that jsdoc preceding an ExpressionStatement like `x.prototype.y;` is also treated as a declaration.
    /// </summary>
    private void BindPrototypePropertyAssignment(Node lhs, Node parentNode)
    {
        // Look up the function in the local scope, since prototype assignments should
        // follow the function declaration
        var classPrototype = store.Expression(lhs);
        var constructorFunction = store.Expression(classPrototype);

        // Fix up parent pointers since we're going to use these nodes before we bind into them
        Utilities.SetParent(store, constructorFunction, classPrototype);
        Utilities.SetParent(store, classPrototype, lhs);
        Utilities.SetParent(store, lhs, parentNode);

        BindPropertyAssignment(constructorFunction, lhs, /*isPrototypeProperty*/ true, /*containerIsClass*/ true);
    }

    private void BindObjectDefinePropertyAssignment(Node node)
    {
        var target = store.Element(store.Arguments(node), 0);
        var namespaceSymbol = LookupSymbolForPropertyAccess(target, container);
        var isToplevel = store.Kind(store.Parent(store.Parent(node))) == SyntaxKind.SourceFile;
        namespaceSymbol = BindPotentiallyMissingNamespaces(namespaceSymbol, target, isToplevel, /*isPrototypeProperty*/ false, /*containerIsClass*/ false);
        BindPotentiallyNewExpandoMemberToNamespace(node, namespaceSymbol, /*isPrototypeProperty*/ false);
    }

    private void BindSpecialPropertyAssignment(Node node)
    {
        var left = store.Left(node);
        var leftExpression = store.Expression(left);
        // Class declarations in Typescript do not allow property declarations
        var parentSymbol = LookupSymbolForPropertyAccess(leftExpression, blockScopeContainer);
        if (parentSymbol.IsNone)
        {
            parentSymbol = LookupSymbolForPropertyAccess(leftExpression, container);
        }
        if (!Utilities.IsInJSFile(store, node) && !IsFunctionSymbol(parentSymbol))
        {
            return;
        }
        var rootExpr = GetLeftmostAccessExpression(left);
        if (store.Kind(rootExpr) == SyntaxKind.Identifier)
        {
            var rootSymbol = LookupSymbolForName(container, store.EscapedText(rootExpr));
            if (!rootSymbol.IsNone && (b.Flags(rootSymbol) & SymbolFlags.Alias) != 0)
            {
                return;
            }
        }
        // Fix up parent pointers since we're going to use these nodes before we bind into them
        Utilities.SetParent(store, left, node);
        Utilities.SetParent(store, store.Right(node), node);
        if (store.Kind(leftExpression) == SyntaxKind.Identifier && container == file && IsExportsOrModuleExportsOrAlias(leftExpression))
        {
            // This can be an alias for the 'exports' or 'module.exports' names, e.g.
            //    var util = module.exports;
            //    util.property = function ...
            BindExportsPropertyAssignment(node);
        }
        else if (Utilities.HasDynamicName(store, node))
        {
            BindAnonymousDeclaration(node, SymbolFlags.Property | SymbolFlags.Assignment, Constant(ref nameComputed, InternalSymbolName.Computed));
            var sym = BindPotentiallyMissingNamespaces(parentSymbol, leftExpression, IsTopLevelNamespaceAssignment(left), /*isPrototypeProperty*/ false, /*containerIsClass*/ false);
            AddLateBoundAssignmentDeclarationToSymbol(node, sym);
        }
        else
        {
            Debug.Assert(Utilities.IsBindableStaticNameExpression(store, left));
            BindStaticPropertyAssignment(left);
        }
    }

    /// <summary>
    /// For nodes like `x.y = z`, declare a member 'y' on 'x' if x is a function (or IIFE) or class or {}, or not declared.
    /// Also works for expression statements preceded by JSDoc, like / ** @type number * / x.y;
    /// </summary>
    private void BindStaticPropertyAssignment(Node node)
    {
        Debug.Assert(store.Kind(node) != SyntaxKind.Identifier);
        Utilities.SetParent(store, store.Expression(node), node);
        BindPropertyAssignment(store.Expression(node), node, /*isPrototypeProperty*/ false, /*containerIsClass*/ false);
    }

    private Symbol BindPotentiallyMissingNamespaces(Symbol namespaceSymbol, Node entityName, bool isToplevel, bool isPrototypeProperty, bool containerIsClass)
    {
        if (!namespaceSymbol.IsNone && (b.Flags(namespaceSymbol) & SymbolFlags.Alias) != 0)
        {
            return namespaceSymbol;
        }
        if (isToplevel && !isPrototypeProperty)
        {
            // make symbols or add declarations for intermediate containers
            namespaceSymbol = ForEachIdentifierInEntityName(entityName, namespaceSymbol, EntityNameAction.DeclareNamespace);
        }
        if (containerIsClass && !namespaceSymbol.IsNone && !b.ValueDeclaration(namespaceSymbol).IsNone)
        {
            AddDeclarationToSymbol(namespaceSymbol, b.ValueDeclaration(namespaceSymbol), SymbolFlags.Class);
        }
        return namespaceSymbol;
    }

    private void BindPotentiallyNewExpandoMemberToNamespace(Node declaration, Symbol namespaceSymbol, bool isPrototypeProperty)
    {
        if (namespaceSymbol.IsNone || !IsExpandoSymbol(namespaceSymbol))
        {
            return;
        }

        // Set up the members collection if it doesn't exist already
        SymbolTable symbolTable;
        if (isPrototypeProperty)
        {
            if (b.Members(namespaceSymbol).IsNone)
            {
                b.SetMembers(namespaceSymbol, b.CreateTable());
            }
            symbolTable = b.Members(namespaceSymbol);
        }
        else
        {
            if (b.Exports(namespaceSymbol).IsNone)
            {
                b.SetExports(namespaceSymbol, b.CreateTable());
            }
            symbolTable = b.Exports(namespaceSymbol);
        }

        var includes = SymbolFlags.None;
        var excludes = SymbolFlags.None;
        // Method-like
        if (Utilities.IsFunctionLikeDeclaration(store, Utilities.GetAssignedExpandoInitializer(store, declaration)))
        {
            includes = SymbolFlags.Method;
            excludes = SymbolFlags.MethodExcludes;
        }
        // Maybe accessor-like
        else if (store.Kind(declaration) == SyntaxKind.CallExpression && Utilities.IsBindableObjectDefinePropertyCall(store, declaration))
        {
            var descriptor = store.Element(store.Arguments(declaration), 2);
            if (HasPropertyNamed(descriptor, "set"))
            {
                // We mix in `SymbolFLags.Property` so in the checker `getTypeOfVariableParameterOrProperty` is used for this
                // symbol, instead of `getTypeOfAccessor` (which will assert as there is no real accessor declaration)
                includes |= SymbolFlags.SetAccessor | SymbolFlags.Property;
                excludes |= SymbolFlags.SetAccessorExcludes;
            }
            if (HasPropertyNamed(descriptor, "get"))
            {
                includes |= SymbolFlags.GetAccessor | SymbolFlags.Property;
                excludes |= SymbolFlags.GetAccessorExcludes;
            }
        }

        if (includes == SymbolFlags.None)
        {
            includes = SymbolFlags.Property;
            excludes = SymbolFlags.PropertyExcludes;
        }

        DeclareSymbol(symbolTable, namespaceSymbol, declaration, includes | SymbolFlags.Assignment, excludes & ~SymbolFlags.Assignment);
    }

    // some(descriptor.properties, p => { const id = getNameOfDeclaration(p); return !!id && isIdentifier(id) && idText(id) === name; })
    // (properties of anything but an object literal are undefined: some(undefined) is false).
    private bool HasPropertyNamed(Node descriptor, string name)
    {
        if (store.Kind(descriptor) != SyntaxKind.ObjectLiteralExpression)
        {
            return false;
        }
        foreach (var p in store.Nodes(store.Properties(descriptor)))
        {
            var id = Utilities.GetNameOfDeclaration(store, p);
            if (!id.IsNone && store.Kind(id) == SyntaxKind.Identifier && texts.Equals(UtilitiesPublic.IdText(store, id), name))
            {
                return true;
            }
        }
        return false;
    }

    private void BindPropertyAssignment(Node name, Node propertyAccess, bool isPrototypeProperty, bool containerIsClass)
    {
        var namespaceSymbol = LookupSymbolForPropertyAccess(name, blockScopeContainer);
        if (namespaceSymbol.IsNone)
        {
            namespaceSymbol = LookupSymbolForPropertyAccess(name, container);
        }
        var isToplevel = IsTopLevelNamespaceAssignment(propertyAccess);
        namespaceSymbol = BindPotentiallyMissingNamespaces(namespaceSymbol, store.Expression(propertyAccess), isToplevel, isPrototypeProperty, containerIsClass);
        BindPotentiallyNewExpandoMemberToNamespace(propertyAccess, namespaceSymbol, isPrototypeProperty);
    }

    /// <summary>
    /// Javascript expando values are:
    /// - Functions
    /// - classes
    /// - namespaces
    /// - variables initialized with function expressions
    /// -                       with class expressions
    /// -                       with empty object literals
    /// -                       with non-empty object literals if assigned to the prototype property
    /// </summary>
    private bool IsExpandoSymbol(Symbol symbol)
    {
        if ((b.Flags(symbol) & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.NamespaceModule)) != 0)
        {
            return true;
        }
        var node = b.ValueDeclaration(symbol);
        if (!node.IsNone && store.Kind(node) == SyntaxKind.CallExpression)
        {
            return !Utilities.GetAssignedExpandoInitializer(store, node).IsNone;
        }
        var init = Node.None;
        if (!node.IsNone)
        {
            var kind = store.Kind(node);
            init = kind == SyntaxKind.VariableDeclaration ? store.Initializer(node)
                : kind == SyntaxKind.BinaryExpression ? store.Right(node)
                : kind == SyntaxKind.PropertyAccessExpression && store.Kind(store.Parent(node)) == SyntaxKind.BinaryExpression ? store.Right(store.Parent(node))
                : Node.None;
        }
        if (!init.IsNone)
        {
            init = Utilities.GetRightMostAssignedExpression(store, init);
        }
        if (!init.IsNone)
        {
            var kind = store.Kind(node);
            var isPrototypeAssignment = Utilities.IsPrototypeAccess(store, kind == SyntaxKind.VariableDeclaration ? store.Name(node) : kind == SyntaxKind.BinaryExpression ? store.Left(node) : node);
            var operand = init;
            if (store.Kind(init) == SyntaxKind.BinaryExpression)
            {
                var op = store.Kind(store.OperatorToken(init));
                if (op == SyntaxKind.BarBarToken || op == SyntaxKind.QuestionQuestionToken)
                {
                    operand = store.Right(init);
                }
            }
            return !Utilities.GetExpandoInitializer(store, operand, isPrototypeAssignment).IsNone;
        }
        return false;
    }

    private Symbol LookupSymbolForPropertyAccess(Node node, Node lookupContainer)
    {
        if (store.Kind(node) == SyntaxKind.Identifier)
        {
            return LookupSymbolForName(lookupContainer, store.EscapedText(node));
        }
        else
        {
            var symbol = LookupSymbolForPropertyAccess(store.Expression(node), container);
            if (symbol.IsNone || b.Exports(symbol).IsNone)
            {
                return Symbol.None;
            }
            var name = Utilities.GetElementOrPropertyAccessName(store, node);
            return name.IsNone ? Symbol.None : b.Get(b.Exports(symbol), name);
        }
    }

    private Symbol ForEachIdentifierInEntityName(Node e, Symbol parentSymbol, EntityNameAction action)
    {
        if (IsExportsOrModuleExportsOrAlias(e))
        {
            return b.SymbolOf(file);
        }
        else if (store.Kind(e) == SyntaxKind.Identifier)
        {
            return EntityNameCallback(action, e, LookupSymbolForPropertyAccess(e, container), parentSymbol);
        }
        else
        {
            var s = ForEachIdentifierInEntityName(store.Expression(e), parentSymbol, action);
            var name = Utilities.GetNameOrArgument(store, e);
            // unreachable
            if (store.Kind(name) == SyntaxKind.PrivateIdentifier)
            {
                Debug.Fail("unexpected PrivateIdentifier");
            }
            var member = Symbol.None;
            if (!s.IsNone && !b.Exports(s).IsNone)
            {
                var memberName = Utilities.GetElementOrPropertyAccessName(store, e);
                member = memberName.IsNone ? Symbol.None : b.Get(b.Exports(s), memberName);
            }
            return EntityNameCallback(action, name, member, s);
        }
    }

    private Symbol EntityNameCallback(EntityNameAction action, Node id, Symbol symbol, Symbol parentSymbol)
    {
        if (action == EntityNameAction.AddModuleDeclaration)
        {
            if (!symbol.IsNone)
            {
                AddDeclarationToSymbol(symbol, id, SymbolFlags.Module | SymbolFlags.Assignment);
            }
            return symbol;
        }
        const SymbolFlags flags = SymbolFlags.Module | SymbolFlags.Assignment;
        const SymbolFlags excludeFlags = SymbolFlags.ValueModuleExcludes & ~SymbolFlags.Assignment;
        if (!symbol.IsNone)
        {
            AddDeclarationToSymbol(symbol, id, flags);
            return symbol;
        }
        else
        {
            SymbolTable table;
            if (!parentSymbol.IsNone)
            {
                table = b.Exports(parentSymbol);
            }
            else
            {
                if (b.JsGlobalAugmentations.IsNone)
                {
                    b.JsGlobalAugmentations = b.CreateTable();
                }
                table = b.JsGlobalAugmentations;
            }
            return DeclareSymbol(table, parentSymbol, id, flags, excludeFlags);
        }
    }

    private void BindCallExpression(Node node)
    {
        // We're only inspecting call expressions to detect CommonJS modules, so we can skip
        // this check if we've already seen the module indicator
        if (commonJsModuleIndicator.IsNone && IsRequireCall(node, /*requireStringLiteralLikeArgument*/ false))
        {
            SetCommonJsModuleIndicator(node);
        }
    }

    /// <summary>utilities.ts: isFunctionSymbol.</summary>
    private bool IsFunctionSymbol(Symbol symbol)
    {
        if (symbol.IsNone || b.ValueDeclaration(symbol).IsNone)
        {
            return false;
        }
        var decl = b.ValueDeclaration(symbol);
        return store.Kind(decl) == SyntaxKind.FunctionDeclaration
            || store.Kind(decl) == SyntaxKind.VariableDeclaration && !store.Initializer(decl).IsNone && Utilities.IsFunctionLike(store, store.Initializer(decl));
    }

    /// <summary>binder.ts: isExportsOrModuleExportsOrAlias(file, node) of the file being bound.</summary>
    private bool IsExportsOrModuleExportsOrAlias(Node node)
    {
        var i = 0;
        var q = exportsAliasQueue;
        q.Clear();
        q.Enqueue(node);
        while (q.Count > 0 && i < 100)
        {
            i++;
            node = q.Dequeue();
            if (Utilities.IsExportsIdentifier(store, node) || Utilities.IsModuleExportsAccessExpression(store, node))
            {
                return true;
            }
            else if (store.Kind(node) == SyntaxKind.Identifier)
            {
                var symbol = LookupSymbolForName(file, store.EscapedText(node));
                if (!symbol.IsNone)
                {
                    var valueDeclaration = b.ValueDeclaration(symbol);
                    if (!valueDeclaration.IsNone && store.Kind(valueDeclaration) == SyntaxKind.VariableDeclaration && !store.Initializer(valueDeclaration).IsNone)
                    {
                        var init = store.Initializer(valueDeclaration);
                        q.Enqueue(init);
                        if (Utilities.IsAssignmentExpression(store, init, /*excludeCompoundAssignment*/ true))
                        {
                            q.Enqueue(store.Left(init));
                            q.Enqueue(store.Right(init));
                        }
                    }
                }
            }
        }
        return false;
    }

    private readonly Queue<Node> exportsAliasQueue = new();

    // "module" as a text of the file's table.
    private TextRef NameModule => Constant(ref nameModule, "module");
}
