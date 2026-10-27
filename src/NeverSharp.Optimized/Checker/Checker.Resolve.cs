// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts (createNameResolver) and src/compiler/checker.ts (resolveName's callbacks
// onFailedToResolveSymbol and onSuccessfullyResolvedSymbol with their checkAndReportErrorFor* helpers, spelling
// suggestions, getResolvedSymbol, resolveEntityName, resolveAlias, getTargetOf*Declaration, getSymbolFlags,
// resolveExternalModuleName, resolveExternalModule, isBlockScopedNameDeclaredBeforeUse)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/binder/nameresolver.go and internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. Names are strings (escaped names); a name argument is a string
// plus the identifier it came from (None for a plain string). Module resolution: Gate C's programs use noResolve, so
// only ambient modules resolve (resolveExternalModule's host lookup finds nothing); `isReferenced` (unused checks) and
// the type-only alias tracking are left to module C8.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private int suggestionCount;
    private const int MaximumSuggestionCount = 10;

    // ---- createNameResolver ----

    /// <summary>
    /// checker.ts resolveName (createNameResolver's resolveNameHelper with the checker's callbacks). <paramref name="nameNode"/>
    /// is the identifier when the name argument is one (its diagnostic name is then its declaration text).
    /// </summary>
    internal SymbolRef ResolveName(NodeRef location, string name, NodeRef nameNode, SymbolFlags meaning, DiagnosticMessage? nameNotFoundMessage, bool isUse, bool excludeGlobals = false) =>
        ResolveNameHelper(location, name, nameNode, meaning, nameNotFoundMessage, isUse, excludeGlobals, forSuggestion: false);

    private SymbolRef ResolveNameHelper(NodeRef location, string name, NodeRef nameNode, SymbolFlags meaning, DiagnosticMessage? nameNotFoundMessage, bool isUse, bool excludeGlobals, bool forSuggestion)
    {
        var originalLocation = location; // needed for did-you-mean error reporting, which gathers candidates starting from the original location
        var result = SymbolRef.None;
        var lastLocation = NodeRef.None;
        var lastSelfReferenceLocation = NodeRef.None;
        var associatedDeclarationForContainingInitializerOrBindingName = NodeRef.None;
        var withinDeferredContext = false;
        var file = location.File;
        var store = location.IsNone ? null : StoreOf(location);
        while (!location.IsNone)
        {
            var node = location.Node;
            var kind = store!.Kind(node);
            if (name == "const" && Utilities.IsConstAssertion(store, node))
            {
                // `const` in an `as const` has no symbol, but issues no error because there is no *actual* lookup of the type
                // (it refers to the constant type of the expression instead)
                return SymbolRef.None;
            }
            if (Utilities.IsModuleOrEnumDeclaration(store, node) && !lastLocation.IsNone && store.Name(node) == lastLocation.Node)
            {
                // If lastLocation is the name of a namespace or enum, skip the parent since it will have is own locals that could
                // conflict.
                lastLocation = location;
                location = Parent(location);
                node = location.Node;
                kind = store.Kind(node);
            }
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            if (Utilities.CanHaveLocals(store, node) && !IsGlobalSourceFile(location))
            {
                var locals = LocalsOf(location);
                if (locals is not null && !(result = Lookup(locals, name, meaning, forSuggestion)).IsNone)
                {
                    var useResult = true;
                    if (Utilities.IsFunctionLike(store, node) && !lastLocation.IsNone && lastLocation.Node != store.Body(node))
                    {
                        // symbol lookup restrictions for function-like declarations
                        // - Type parameters of a function are in scope in the entire function declaration, including the parameter
                        //   list and return type. However, local types are only in scope in the function body.
                        // - parameters are only in the scope of function body
                        // This restriction does not apply to JSDoc comment types because they are parented
                        // at a higher level than type parameters would normally be
                        var resultFlags = SymbolFlagsOf(result);
                        var lastKind = store.Kind(lastLocation.Node);
                        if ((meaning & resultFlags & SymbolFlags.Type) != 0 && lastKind != SyntaxKind.JSDoc)
                        {
                            useResult = (resultFlags & SymbolFlags.TypeParameter) != 0
                                // type parameters are visible in parameter list, return type and type parameter list
                                ? (store.Flags(lastLocation.Node) & NodeFlags.Synthesized) != 0 ||
                                    lastLocation.Node == store.Type(node) ||
                                    lastKind == SyntaxKind.Parameter ||
                                    lastKind == SyntaxKind.JSDocParameterTag ||
                                    lastKind == SyntaxKind.JSDocReturnTag ||
                                    lastKind == SyntaxKind.TypeParameter
                                // local types not visible outside the function body
                                : false;
                        }
                        if ((meaning & resultFlags & SymbolFlags.Variable) != 0)
                        {
                            // expression inside parameter will lookup as normal variable scope when targeting es2015+
                            if (UseOuterVariableScopeInParameter(result, location, lastLocation))
                            {
                                useResult = false;
                            }
                            else if ((resultFlags & SymbolFlags.FunctionScopedVariable) != 0)
                            {
                                // parameters are visible only inside function body, parameter list and return type
                                // technically for parameter list case here we might mix parameters and variables declared in function,
                                // however it is detected separately when checking initializers of parameters
                                // to make sure that they reference no variables declared after them.
                                useResult = lastKind == SyntaxKind.Parameter ||
                                    (store.Flags(lastLocation.Node) & NodeFlags.Synthesized) != 0 ||
                                    (lastLocation.Node == store.Type(node) && HasParameterAncestor(ValueDeclarationOf(result)));
                            }
                        }
                    }
                    else if (kind == SyntaxKind.ConditionalType)
                    {
                        // A type parameter declared using 'infer T' in a conditional type is visible only in
                        // the true branch of the conditional type.
                        useResult = lastLocation.Node == store.TrueType(node);
                    }
                    if (useResult)
                    {
                        break;
                    }
                    result = SymbolRef.None;
                }
            }
            withinDeferredContext = withinDeferredContext || GetIsDeferredContext(location, lastLocation);
            var found = false;
            switch (kind)
            {
                case SyntaxKind.SourceFile:
                case SyntaxKind.ModuleDeclaration:
                {
                    if (kind == SyntaxKind.SourceFile && !Utilities.IsExternalOrCommonJsModule(store, node, BindingOf(file)))
                    {
                        break;
                    }
                    var moduleSymbol = GetSymbolOfDeclaration(location);
                    var moduleExports = (moduleSymbol.IsNone ? null : ExportsOf(moduleSymbol)) ?? emptySymbols;
                    if (kind == SyntaxKind.SourceFile || ((store.Flags(node) & NodeFlags.Ambient) != 0 && !Utilities.IsGlobalScopeAugmentation(store, node)))
                    {
                        // It's an external module. First see if the module has an export default and if the local
                        // name of that export default matches.
                        if (!(result = moduleExports.Get(InternalSymbolName.Default)).IsNone)
                        {
                            var localSymbol = GetLocalSymbolForExportDefault(result);
                            if (!localSymbol.IsNone && (SymbolFlagsOf(result) & meaning) != 0 && NameOf(localSymbol) == name)
                            {
                                found = true;
                                break;
                            }
                            result = SymbolRef.None;
                        }

                        // Because of module/namespace merging, a module's exports are in scope,
                        // yet we never want to treat an export specifier as putting a member in scope.
                        // Therefore, if the name we find is purely an export specifier, it is not actually considered in scope.
                        var moduleExport = moduleExports.Get(name);
                        if (!moduleExport.IsNone && SymbolFlagsOf(moduleExport) == SymbolFlags.Alias &&
                            (!GetDeclarationOfKind(moduleExport, SyntaxKind.ExportSpecifier).IsNone || !GetDeclarationOfKind(moduleExport, SyntaxKind.NamespaceExport).IsNone))
                        {
                            break;
                        }
                    }

                    // ES6 exports are also visible locally (except for 'default'), but commonjs exports are not (except typedefs)
                    if (name != InternalSymbolName.Default && !(result = Lookup(moduleExports, name, meaning & SymbolFlags.ModuleMember, forSuggestion)).IsNone)
                    {
                        if (kind == SyntaxKind.SourceFile && !BindingOf(file).CommonJsModuleIndicator.IsNone && !(DeclarationsOf(result) ?? []).Exists(d => Utilities.IsJSDocTypeAlias(StoreOf(d), d.Node)))
                        {
                            result = SymbolRef.None;
                        }
                        else
                        {
                            found = true;
                        }
                    }
                    break;
                }
                case SyntaxKind.EnumDeclaration:
                {
                    var enumSymbol = GetSymbolOfDeclaration(location);
                    if (!(result = Lookup((enumSymbol.IsNone ? null : ExportsOf(enumSymbol)) ?? emptySymbols, name, meaning & SymbolFlags.EnumMember, forSuggestion)).IsNone)
                    {
                        // (Cannot_access_0_from_another_file_without_qualification_when_1_is_enabled: isolatedModules, not set in Gate C)
                        found = true;
                    }
                    break;
                }
                case SyntaxKind.PropertyDeclaration:
                    // TypeScript 1.0 spec (April 2014): 8.4.1 (propertyWithInvalidInitializer: only reported without
                    // emitStandardClassFields, which Gate C's target ES2025 enables)
                    break;
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                {
                    // The below is used to lookup type parameters within a class or interface, as they are added to the class/interface locals
                    // These can never be latebound, so the symbol's raw members are sufficient.
                    var classSymbol = GetSymbolOfDeclaration(location);
                    if (!(result = Lookup((classSymbol.IsNone ? null : MembersOf(classSymbol)) ?? emptySymbols, name, meaning & SymbolFlags.Type, forSuggestion)).IsNone)
                    {
                        if (!IsTypeParameterSymbolDeclaredInContainer(result, location))
                        {
                            // ignore type parameters not declared in this container
                            result = SymbolRef.None;
                            break;
                        }
                        if (!lastLocation.IsNone && Utilities.IsStatic(store, lastLocation.Node))
                        {
                            // TypeScript 1.0 spec (April 2014): 3.4.1
                            // The scope of a type parameter extends over the entire declaration with which the type
                            // parameter list is associated, with the exception of static member declarations in classes.
                            if (nameNotFoundMessage is not null)
                            {
                                Error(originalLocation, Diagnostics.Static_members_cannot_reference_class_type_parameters);
                            }
                            return SymbolRef.None;
                        }
                        found = true;
                        break;
                    }
                    if (kind == SyntaxKind.ClassExpression && (meaning & SymbolFlags.Class) != 0)
                    {
                        var className = store.Name(node);
                        if (!className.IsNone && store.Texts.Equals(store.EscapedText(className), name))
                        {
                            result = NodeSymbol(location);
                            found = true;
                        }
                    }
                    break;
                }
                case SyntaxKind.ExpressionWithTypeArguments:
                {
                    // The type parameters of a class are not in scope in the base class expression.
                    var parent = store.Parent(node);
                    if (lastLocation.Node == store.Expression(node) && store.Token(parent) == SyntaxKind.ExtendsKeyword)
                    {
                        var container = store.Parent(parent);
                        if (Utilities.IsClassLike(store, container) &&
                            !(result = Lookup(MembersOf(GetSymbolOfDeclaration(At(location, container))), name, meaning & SymbolFlags.Type, forSuggestion)).IsNone)
                        {
                            if (nameNotFoundMessage is not null)
                            {
                                Error(originalLocation, Diagnostics.Base_class_expressions_cannot_reference_class_type_parameters);
                            }
                            return SymbolRef.None;
                        }
                    }
                    break;
                }
                case SyntaxKind.ComputedPropertyName:
                {
                    // It is not legal to reference a class's own type parameters from a computed property name that
                    // belongs to the class.
                    var grandparent = store.Parent(store.Parent(node));
                    if (Utilities.IsClassLike(store, grandparent) || store.Kind(grandparent) == SyntaxKind.InterfaceDeclaration)
                    {
                        // A reference to this grandparent's type parameters would be an error
                        if (!(result = Lookup(MembersOf(GetSymbolOfDeclaration(At(location, grandparent))), name, meaning & SymbolFlags.Type, forSuggestion)).IsNone)
                        {
                            if (nameNotFoundMessage is not null)
                            {
                                Error(originalLocation, Diagnostics.A_computed_property_name_cannot_reference_a_type_parameter_from_its_containing_type);
                            }
                            return SymbolRef.None;
                        }
                    }
                    break;
                }
                case SyntaxKind.ArrowFunction:
                    // when targeting ES6 or higher there is no 'arguments' in an arrow function
                    // for lower compile targets the resolved symbol is used to emit an error
                    if (languageVersion >= ScriptTarget.ES2015)
                    {
                        break;
                    }
                    goto case SyntaxKind.FunctionDeclaration;
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionDeclaration:
                    if ((meaning & SymbolFlags.Variable) != 0 && name == "arguments")
                    {
                        result = argumentsSymbol;
                        found = true;
                    }
                    break;
                case SyntaxKind.FunctionExpression:
                    if ((meaning & SymbolFlags.Variable) != 0 && name == "arguments")
                    {
                        result = argumentsSymbol;
                        found = true;
                        break;
                    }
                    if ((meaning & SymbolFlags.Function) != 0)
                    {
                        var functionName = store.Name(node);
                        if (!functionName.IsNone && store.Texts.Equals(store.EscapedText(functionName), name))
                        {
                            result = NodeSymbol(location);
                            found = true;
                        }
                    }
                    break;
                case SyntaxKind.Decorator:
                {
                    // Decorators are resolved at the class declaration. Resolving at the parameter
                    // or member would result in looking up locals in the method.
                    var parent = store.Parent(node);
                    if (!parent.IsNone && store.Kind(parent) == SyntaxKind.Parameter)
                    {
                        location = Parent(location);
                        node = location.Node;
                        parent = store.Parent(node);
                    }
                    // class Decorators are resolved outside of the class to avoid referencing type parameters of that class.
                    if (!parent.IsNone && (Utilities.IsClassElement(store, parent) || store.Kind(parent) == SyntaxKind.ClassDeclaration))
                    {
                        location = Parent(location);
                    }
                    break;
                }
                case SyntaxKind.JSDocTypedefTag:
                case SyntaxKind.JSDocCallbackTag:
                case SyntaxKind.JSDocEnumTag:
                case SyntaxKind.JSDocImportTag:
                {
                    // js type aliases do not resolve names from their host, so skip past it
                    var root = GetJSDocRoot(store, node);
                    if (!root.IsNone)
                    {
                        location = At(location, store.Parent(root));
                    }
                    break;
                }
                case SyntaxKind.Parameter:
                    if (!lastLocation.IsNone &&
                        (lastLocation.Node == store.Initializer(node) || (lastLocation.Node == store.Name(node) && Utilities.IsBindingPattern(store, lastLocation.Node))))
                    {
                        if (associatedDeclarationForContainingInitializerOrBindingName.IsNone)
                        {
                            associatedDeclarationForContainingInitializerOrBindingName = location;
                        }
                    }
                    break;
                case SyntaxKind.BindingElement:
                    if (!lastLocation.IsNone &&
                        (lastLocation.Node == store.Initializer(node) || (lastLocation.Node == store.Name(node) && Utilities.IsBindingPattern(store, lastLocation.Node))))
                    {
                        if (Utilities.IsPartOfParameterDeclaration(store, node) && associatedDeclarationForContainingInitializerOrBindingName.IsNone)
                        {
                            associatedDeclarationForContainingInitializerOrBindingName = location;
                        }
                    }
                    break;
                case SyntaxKind.InferType:
                    if ((meaning & SymbolFlags.TypeParameter) != 0)
                    {
                        var typeParameter = store.TypeParameter(node);
                        var parameterName = store.Name(typeParameter);
                        if (!parameterName.IsNone && store.Texts.Equals(store.EscapedText(parameterName), name))
                        {
                            result = NodeSymbol(At(location, typeParameter));
                            found = true;
                        }
                    }
                    break;
                case SyntaxKind.ExportSpecifier:
                    // External module export bindings shouldn't be resolved to local symbols.
                    if (!lastLocation.IsNone && lastLocation.Node == store.PropertyName(node) &&
                        !store.ModuleSpecifier(store.Parent(store.Parent(node))).IsNone)
                    {
                        location = Parent(Parent(Parent(location)));
                    }
                    break;
            }
            if (found)
            {
                break;
            }
            if (IsSelfReferenceLocation(location, lastLocation))
            {
                lastSelfReferenceLocation = location;
            }
            lastLocation = location;
            var locationKind = store.Kind(location.Node);
            if (locationKind == SyntaxKind.JSDocTemplateTag)
            {
                var container = Utilities.GetEffectiveContainerForJSDocTemplateTag(store, location.Node);
                location = !container.IsNone ? At(location, container) : Parent(location);
            }
            else if (locationKind is SyntaxKind.JSDocParameterTag or SyntaxKind.JSDocReturnTag)
            {
                var host = Utilities.GetHostSignatureFromJSDoc(store, location.Node);
                location = !host.IsNone ? At(location, host) : Parent(location);
            }
            else
            {
                location = Parent(location);
            }
        }

        // We just climbed up parents looking for the name, meaning that we started in a descendant node of `lastLocation`.
        // If `result === lastSelfReferenceLocation.symbol`, that means that we are somewhere inside `lastSelfReferenceLocation` looking up a name, and resolving to `lastLocation` itself.
        // That means that this is a self-reference of `lastLocation`, and shouldn't count this when considering whether `lastLocation` is used.
        if (isUse && !forSuggestion && !result.IsNone && (lastSelfReferenceLocation.IsNone || result != NodeSymbol(lastSelfReferenceLocation)))
        {
            MarkReferenced(result, meaning);
        }

        if (result.IsNone)
        {
            if (!lastLocation.IsNone)
            {
                var binding = BindingOf(lastLocation.File);
                if (!binding.CommonJsModuleIndicator.IsNone && name == "exports")
                {
                    var fileSymbol = NodeSymbol(lastLocation);
                    if (!fileSymbol.IsNone && (meaning & SymbolFlagsOf(fileSymbol)) != 0)
                    {
                        return fileSymbol;
                    }
                }
            }
            if (!excludeGlobals)
            {
                result = Lookup(globals, name, meaning, forSuggestion);
            }
        }
        if (result.IsNone && !originalLocation.IsNone && Utilities.IsInJSFile(StoreOf(originalLocation), originalLocation.Node))
        {
            var originalParent = StoreOf(originalLocation).Parent(originalLocation.Node);
            if (!originalParent.IsNone && IsRequireCall(At(originalLocation, originalParent), requireStringLiteralLikeArgument: false))
            {
                return requireSymbol;
            }
        }

        if (nameNotFoundMessage is not null)
        {
            if (result.IsNone)
            {
                OnFailedToResolveSymbol(originalLocation, name, nameNode, meaning, nameNotFoundMessage);
            }
            else
            {
                OnSuccessfullyResolvedSymbol(originalLocation, result, meaning, lastLocation, associatedDeclarationForContainingInitializerOrBindingName, withinDeferredContext);
            }
        }
        return result;
    }

    // resolveNameHelper's lookup: getSymbol, or getSuggestionForSymbolNameLookup for resolveNameForSymbolSuggestion.
    private SymbolRef Lookup(SymTable? symbols, string name, SymbolFlags meaning, bool forSuggestion)
    {
        if (!forSuggestion)
        {
            return GetSymbol(symbols, name, meaning);
        }
        return GetSuggestionForSymbolNameLookup(symbols ?? emptySymbols, name, meaning);
    }

    private bool HasParameterAncestor(NodeRef node)
    {
        if (node.IsNone)
        {
            return false;
        }
        var store = StoreOf(node);
        for (var n = node.Node; !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) == SyntaxKind.Parameter)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsGlobalSourceFile(NodeRef node) =>
        Kind(node) == SyntaxKind.SourceFile && !Utilities.IsExternalOrCommonJsModule(StoreOf(node), node.Node, BindingOf(node.File));

    // utilities.ts getJSDocRoot: the JSDoc node a tag belongs to.
    private static Node GetJSDocRoot(NodeStore store, Node node)
    {
        for (var n = store.Parent(node); !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) == SyntaxKind.JSDoc)
            {
                return n;
            }
            if (!Utilities.IsJSDocNode(store, n))
            {
                return Node.None;
            }
        }
        return Node.None;
    }

    private bool UseOuterVariableScopeInParameter(SymbolRef result, NodeRef location, NodeRef lastLocation)
    {
        var store = StoreOf(location);
        var body = store.Body(location.Node);
        var valueDeclaration = ValueDeclarationOf(result);
        if (store.Kind(lastLocation.Node) == SyntaxKind.Parameter && !body.IsNone && !valueDeclaration.IsNone && valueDeclaration.File == location.File
            && StoreOf(valueDeclaration).Pos(valueDeclaration.Node) >= store.Pos(body)
            && StoreOf(valueDeclaration).End(valueDeclaration.Node) <= store.End(body))
        {
            // check for several cases where we introduce temporaries that require moving the name/initializer of the parameter to the body
            if (languageVersion >= ScriptTarget.ES2015)
            {
                ref var links = ref NodeLinks.Get(location);
                if (links.DeclarationRequiresScopeChange == 0)
                {
                    var requires = false;
                    foreach (var parameter in store.Nodes(store.Parameters(location.Node)))
                    {
                        if (RequiresScopeChange(store, parameter))
                        {
                            requires = true;
                            break;
                        }
                    }
                    NodeLinks.Get(location).DeclarationRequiresScopeChange = (sbyte)(requires ? 2 : 1);
                }
                return NodeLinks.Get(location).DeclarationRequiresScopeChange == 1;
            }
        }
        return false;
    }

    private bool RequiresScopeChange(NodeStore store, Node parameter) =>
        RequiresScopeChangeWorker(store, store.Name(parameter)) || (!store.Initializer(parameter).IsNone && RequiresScopeChangeWorker(store, store.Initializer(parameter)));

    private bool RequiresScopeChangeWorker(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
                // do not descend into these
                return false;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.PropertyAssignment:
                return RequiresScopeChangeWorker(store, store.Name(node));
            case SyntaxKind.PropertyDeclaration:
                // static properties in classes introduce temporary variables
                if (Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Static))
                {
                    return !EmitStandardClassFields;
                }
                return RequiresScopeChangeWorker(store, store.Name(node));
            default:
                // null coalesce and optional chain pre-es2020 produce temporary variables
                if (Utilities.IsNullishCoalesce(store, node) || UtilitiesPublic.IsOptionalChain(store, node))
                {
                    return languageVersion < ScriptTarget.ES2020;
                }
                if (store.Kind(node) == SyntaxKind.BindingElement && !store.DotDotDotToken(node).IsNone && store.Kind(store.Parent(node)) == SyntaxKind.ObjectBindingPattern)
                {
                    return languageVersion < ScriptTarget.ES2017;
                }
                if (IsTypeNodeKind(store.Kind(node)))
                {
                    return false;
                }
                var any = false;
                NodeVisitor.ForEachChild<object>(store, node, child =>
                {
                    if (!any && RequiresScopeChangeWorker(store, child))
                    {
                        any = true;
                    }
                    return any ? true : null;
                });
                return any;
        }
    }

    private bool EmitStandardClassFields => compilerOptions.GetUseDefineForClassFields() && languageVersion >= ScriptTarget.ES2022;

    /// <summary>utilitiesPublic.ts isTypeNodeKind.</summary>
    internal static bool IsTypeNodeKind(SyntaxKind kind) =>
        (kind >= SyntaxKind.FirstTypeNode && kind <= SyntaxKind.LastTypeNode)
        || kind is SyntaxKind.AnyKeyword or SyntaxKind.UnknownKeyword or SyntaxKind.NumberKeyword or SyntaxKind.BigIntKeyword
            or SyntaxKind.ObjectKeyword or SyntaxKind.BooleanKeyword or SyntaxKind.StringKeyword or SyntaxKind.SymbolKeyword
            or SyntaxKind.VoidKeyword or SyntaxKind.UndefinedKeyword or SyntaxKind.NeverKeyword or SyntaxKind.IntrinsicKeyword
            or SyntaxKind.ExpressionWithTypeArguments or SyntaxKind.JSDocAllType or SyntaxKind.JSDocUnknownType
            or SyntaxKind.JSDocNullableType or SyntaxKind.JSDocNonNullableType or SyntaxKind.JSDocOptionalType
            or SyntaxKind.JSDocFunctionType or SyntaxKind.JSDocVariadicType;

    private bool GetIsDeferredContext(NodeRef location, NodeRef lastLocation)
    {
        var store = StoreOf(location);
        var node = location.Node;
        var kind = store.Kind(node);
        if (kind != SyntaxKind.ArrowFunction && kind != SyntaxKind.FunctionExpression)
        {
            // initializers in instance property declaration of class like entities are executed in constructor and thus deferred
            return kind == SyntaxKind.TypeQuery || ((
                Utilities.IsFunctionLikeDeclaration(store, node) ||
                (kind == SyntaxKind.PropertyDeclaration && !Utilities.IsStatic(store, node))
            ) && (lastLocation.IsNone || lastLocation.Node != store.Name(node))); // A name is evaluated within the enclosing scope - so it shouldn't count as deferred
        }
        if (!lastLocation.IsNone && lastLocation.Node == store.Name(node))
        {
            return false;
        }
        // generator functions and async functions are not inlined in control flow when immediately invoked
        if (!store.AsteriskToken(node).IsNone || Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Async))
        {
            return true;
        }
        return Utilities.GetImmediatelyInvokedFunctionExpression(store, node).IsNone;
    }

    private bool IsSelfReferenceLocation(NodeRef node, NodeRef lastLocation)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.Parameter:
                return !lastLocation.IsNone && lastLocation.Node == store.Name(node.Node);
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.ModuleDeclaration: // For `namespace N { N; }`
                return true;
            default:
                return false;
        }
    }

    private bool IsTypeParameterSymbolDeclaredInContainer(SymbolRef symbol, NodeRef container)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) == SyntaxKind.TypeParameter)
            {
                var store = StoreOf(declaration);
                var parent = store.Parent(declaration.Node);
                if (store.Kind(parent) == SyntaxKind.JSDocTemplateTag)
                {
                    parent = Utilities.GetJSDocHost(store, parent);
                }
                if (declaration.File == container.File && parent == container.Node)
                {
                    // (JSDoc template tags of type aliases: module C9)
                    return true;
                }
            }
        }
        return false;
    }

    // ---- the checker's callbacks ----

    /// <summary>checker.ts diagnosticName(nameArg).</summary>
    private string DiagnosticName(string name, NodeRef nameNode) =>
        nameNode.IsNone ? Utilities.UnescapeLeadingUnderscores(name) : Utilities.DeclarationNameToString(StoreOf(nameNode), nameNode.Node);

    private void OnFailedToResolveSymbol(NodeRef errorLocation, string name, NodeRef nameNode, SymbolFlags meaning, DiagnosticMessage nameNotFoundMessage)
    {
        AddLazyDiagnostic(() =>
        {
            if (errorLocation.IsNone ||
                (Kind(Parent(errorLocation)) != SyntaxKind.JSDocLink &&
                    !CheckAndReportErrorForMissingPrefix(errorLocation, name, nameNode) &&
                    !CheckAndReportErrorForExtendingInterface(errorLocation) &&
                    !CheckAndReportErrorForUsingTypeAsNamespace(errorLocation, name, meaning) &&
                    !CheckAndReportErrorForExportingPrimitiveType(errorLocation, name) &&
                    !CheckAndReportErrorForUsingNamespaceAsTypeOrValue(errorLocation, name, meaning) &&
                    !CheckAndReportErrorForUsingTypeAsValue(errorLocation, name, meaning) &&
                    !CheckAndReportErrorForUsingValueAsType(errorLocation, name, meaning)))
            {
                var suggestion = SymbolRef.None;
                string? suggestedLib = null;
                // Report missing lib first
                suggestedLib = GetSuggestedLibForNonExistentName(DiagnosticName(name, nameNode));
                if (suggestedLib is not null)
                {
                    Error(errorLocation, nameNotFoundMessage, DiagnosticName(name, nameNode), suggestedLib);
                }
                // then spelling suggestions
                if (suggestedLib is null && suggestionCount < MaximumSuggestionCount)
                {
                    suggestion = GetSuggestedSymbolForNonexistentSymbol(errorLocation, name, meaning);
                    var suggestionValueDeclaration = suggestion.IsNone ? NodeRef.None : ValueDeclarationOf(suggestion);
                    if (!suggestionValueDeclaration.IsNone && Utilities.IsAmbientModule(StoreOf(suggestionValueDeclaration), suggestionValueDeclaration.Node)
                        && Utilities.IsGlobalScopeAugmentation(StoreOf(suggestionValueDeclaration), suggestionValueDeclaration.Node))
                    {
                        suggestion = SymbolRef.None;
                    }
                    if (!suggestion.IsNone)
                    {
                        var suggestionName = SymbolToString(suggestion);
                        // (isUncheckedJSSuggestion: JavaScript files, module C9)
                        var message = meaning == SymbolFlags.Namespace ? Diagnostics.Cannot_find_namespace_0_Did_you_mean_1 : Diagnostics.Cannot_find_name_0_Did_you_mean_1;
                        var diagnostic = CreateDiagnosticForNode(errorLocation, message, DiagnosticName(name, nameNode), suggestionName);
                        if (!suggestionValueDeclaration.IsNone)
                        {
                            diagnostic.RelatedInformation = [CreateRelatedForNode(suggestionValueDeclaration, Diagnostics._0_is_declared_here, suggestionName)];
                        }
                        diagnostics.Add(diagnostic);
                    }
                }
                // And then fall back to unspecified "not found"
                if (suggestion.IsNone && suggestedLib is null)
                {
                    Error(errorLocation, nameNotFoundMessage, DiagnosticName(name, nameNode));
                }
                suggestionCount++;
            }
        });
    }

    private void OnSuccessfullyResolvedSymbol(NodeRef errorLocation, SymbolRef result, SymbolFlags meaning, NodeRef lastLocation,
        NodeRef associatedDeclarationForContainingInitializerOrBindingName, bool withinDeferredContext)
    {
        AddLazyDiagnostic(() =>
        {
            var name = NameOf(result);
            var isInExternalModule = !lastLocation.IsNone && Kind(lastLocation) == SyntaxKind.SourceFile &&
                Utilities.IsExternalOrCommonJsModule(StoreOf(lastLocation), lastLocation.Node, BindingOf(lastLocation.File));
            // Only check for block-scoped variable if we have an error location and are looking for the
            // name with variable meaning
            if (!errorLocation.IsNone &&
                ((meaning & SymbolFlags.BlockScopedVariable) != 0 ||
                    (((meaning & SymbolFlags.Class) != 0 || (meaning & SymbolFlags.Enum) != 0) && (meaning & SymbolFlags.Value) == SymbolFlags.Value)))
            {
                var exportOrLocalSymbol = GetExportSymbolOfValueSymbolIfExported(result);
                var flags = SymbolFlagsOf(exportOrLocalSymbol);
                if ((flags & SymbolFlags.BlockScopedVariable) != 0 || (flags & SymbolFlags.Class) != 0 || (flags & SymbolFlags.Enum) != 0)
                {
                    CheckResolvedBlockScopedVariable(exportOrLocalSymbol, errorLocation);
                }
            }

            // If we're in an external module, we can't reference value symbols created from UMD export declarations
            if (isInExternalModule && (meaning & SymbolFlags.Value) == SymbolFlags.Value && (StoreOf(errorLocation).Flags(errorLocation.Node) & NodeFlags.JSDoc) == 0)
            {
                var merged = GetMergedSymbol(result);
                var mergedDeclarations = DeclarationsOf(merged);
                if (mergedDeclarations is { Count: > 0 } && mergedDeclarations.TrueForAll(d => Kind(d) == SyntaxKind.NamespaceExportDeclaration ||
                        (Kind(d) == SyntaxKind.SourceFile && !NodeSymbol(d).IsNone && !BindingOf(d.File).GlobalExports(NodeSymbol(d).BinderSymbol).IsNone)))
                {
                    ErrorOrSuggestion(compilerOptions.AllowUmdGlobalAccess != true, errorLocation, Diagnostics._0_refers_to_a_UMD_global_but_the_current_file_is_a_module_Consider_adding_an_import_instead, Utilities.UnescapeLeadingUnderscores(name));
                }
            }

            // If we're in a parameter initializer or binding name, we can't reference the values of the parameter whose initializer we're within or parameters to the right
            if (!associatedDeclarationForContainingInitializerOrBindingName.IsNone && !withinDeferredContext && (meaning & SymbolFlags.Value) == SymbolFlags.Value)
            {
                var candidate = GetMergedSymbol(result);
                var associated = associatedDeclarationForContainingInitializerOrBindingName;
                var associatedStore = StoreOf(associated);
                var root = At(associated, Utilities.GetRootDeclaration(associatedStore, associated.Node));
                // A parameter initializer or binding pattern initializer within a parameter cannot refer to itself
                if (candidate == GetSymbolOfDeclaration(associated))
                {
                    Error(errorLocation, Diagnostics.Parameter_0_cannot_reference_itself, Utilities.DeclarationNameToString(associatedStore, associatedStore.Name(associated.Node)));
                }
                // And it cannot refer to any declarations which come after it
                else
                {
                    var candidateValueDeclaration = ValueDeclarationOf(candidate);
                    if (!candidateValueDeclaration.IsNone && candidateValueDeclaration.File == associated.File &&
                        associatedStore.Pos(candidateValueDeclaration.Node) > associatedStore.Pos(associated.Node))
                    {
                        var rootParent = Parent(root);
                        var rootLocals = LocalsOf(rootParent);
                        if (rootLocals is not null && GetSymbol(rootLocals, NameOf(candidate), meaning) == candidate)
                        {
                            Error(errorLocation, Diagnostics.Parameter_0_cannot_reference_identifier_1_declared_after_it,
                                Utilities.DeclarationNameToString(associatedStore, associatedStore.Name(associated.Node)),
                                Utilities.DeclarationNameToString(StoreOf(errorLocation), errorLocation.Node));
                        }
                    }
                }
            }
            if (!errorLocation.IsNone && (meaning & SymbolFlags.Value) != 0 && (SymbolFlagsOf(result) & SymbolFlags.Alias) != 0
                && (SymbolFlagsOf(result) & SymbolFlags.Value) == 0 && !IsValidTypeOnlyAliasUseSite(errorLocation))
            {
                var typeOnlyDeclaration = GetTypeOnlyAliasDeclaration(result, SymbolFlags.Value);
                if (!typeOnlyDeclaration.IsNone)
                {
                    var typeOnlyKind = Kind(typeOnlyDeclaration);
                    var isExport = typeOnlyKind is SyntaxKind.ExportSpecifier or SyntaxKind.ExportDeclaration or SyntaxKind.NamespaceExport;
                    var message = isExport
                        ? Diagnostics._0_cannot_be_used_as_a_value_because_it_was_exported_using_export_type
                        : Diagnostics._0_cannot_be_used_as_a_value_because_it_was_imported_using_import_type;
                    var unescapedName = Utilities.UnescapeLeadingUnderscores(name);
                    var d = Error(errorLocation, message, unescapedName);
                    d.RelatedInformation = [CreateRelatedForNode(typeOnlyDeclaration, isExport ? Diagnostics._0_was_exported_here : Diagnostics._0_was_imported_here, unescapedName)];
                }
            }
            // (isolatedModules import conflicts: not set in Gate C)
        });
    }

    /// <summary>utilities.ts isValidTypeOnlyAliasUseSite (heritage clauses and computed names: the common cases).</summary>
    private bool IsValidTypeOnlyAliasUseSite(NodeRef useSite)
    {
        var store = StoreOf(useSite);
        return (store.Flags(useSite.Node) & NodeFlags.Ambient) != 0
            || Utilities.IsPartOfTypeQuery(store, useSite.Node)
            || IsIdentifierInNonEmittingHeritageClause(store, useSite.Node)
            || !(Utilities.IsExpressionNode(store, useSite.Node) || IsShorthandPropertyNameUseSite(store, useSite.Node));
    }

    private static bool IsShorthandPropertyNameUseSite(NodeStore store, Node useSite) =>
        store.Kind(useSite) == SyntaxKind.Identifier && store.Kind(store.Parent(useSite)) == SyntaxKind.ShorthandPropertyAssignment && store.Name(store.Parent(useSite)) == useSite;

    // utilities.ts isIdentifierInNonEmittingHeritageClause
    private static bool IsIdentifierInNonEmittingHeritageClause(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.Identifier)
        {
            return false;
        }
        var n = store.Parent(node);
        while (store.Kind(n) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ExpressionWithTypeArguments)
        {
            n = store.Parent(n);
        }
        if (store.Kind(n) != SyntaxKind.HeritageClause)
        {
            return false;
        }
        return store.Token(n) == SyntaxKind.ImplementsKeyword || store.Kind(store.Parent(n)) == SyntaxKind.InterfaceDeclaration;
    }

    /// <summary>
    /// checker.ts getTypeOnlyAliasDeclaration: indicates that a symbol directly or indirectly resolves to a type-only
    /// import or export. (Type-only export stars, `export type * from`, need modules Gate C does not resolve: none.)
    /// </summary>
    private NodeRef GetTypeOnlyAliasDeclaration(SymbolRef symbol, SymbolFlags include = SymbolFlags.None)
    {
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) == 0)
        {
            return NodeRef.None;
        }
        if (AliasSymbolLinks.Get(symbol).TypeOnlyState == 0)
        {
            // We need to set a WIP value here to prevent reentrancy during `getImmediateAliasedSymbol` which, paradoxically, can depend on this
            AliasSymbolLinks.Get(symbol).TypeOnlyState = 1;
            var resolved = ResolveSymbol(symbol); // do this before the `resolveImmediate` below, as it uses a different circularity cache and we might hide a circularity error if we blindly get the immediate alias first
            // While usually the alias will have been marked during the pass by the full typecheck, we may still need to calculate the alias declaration now
            var declarations = DeclarationsOf(symbol);
            var immediate = !GetDeclarationOfAliasSymbol(symbol).IsNone ? GetImmediateAliasedSymbol(symbol) : SymbolRef.None;
            MarkSymbolOfAliasDeclarationIfTypeOnly(declarations is { Count: > 0 } ? declarations[0] : NodeRef.None, immediate, resolved, overwriteEmpty: true);
        }
        ref var links = ref AliasSymbolLinks.Get(symbol);
        if (links.TypeOnlyState != 2)
        {
            return NodeRef.None;
        }
        if (include == SymbolFlags.None)
        {
            return links.TypeOnlyDeclaration;
        }
        var typeOnlyDeclaration = links.TypeOnlyDeclaration;
        var resolvedTypeOnly = ResolveAlias(GetSymbolOfDeclaration(typeOnlyDeclaration));
        return (GetSymbolFlags(resolvedTypeOnly) & include) != 0 ? typeOnlyDeclaration : NodeRef.None;
    }

    /// <summary>checker.ts markSymbolOfAliasDeclarationIfTypeOnly: whether the alias is (or resolves to) a type-only alias.</summary>
    private bool MarkSymbolOfAliasDeclarationIfTypeOnly(NodeRef aliasDeclaration, SymbolRef immediateTarget, SymbolRef finalTarget, bool overwriteEmpty)
    {
        if (aliasDeclaration.IsNone || Kind(aliasDeclaration) == SyntaxKind.PropertyAccessExpression)
        {
            return false;
        }
        // If the declaration itself is type-only, mark it and return.
        // No need to check what it resolves to.
        var sourceSymbol = GetSymbolOfDeclaration(aliasDeclaration);
        if (sourceSymbol.IsNone)
        {
            return false;
        }
        if (IsTypeOnlyImportOrExportDeclaration(aliasDeclaration))
        {
            ref var declLinks = ref AliasSymbolLinks.Get(sourceSymbol);
            declLinks.TypeOnlyDeclaration = aliasDeclaration;
            declLinks.TypeOnlyState = 2;
            return true;
        }
        return MarkSymbolOfAliasDeclarationIfTypeOnlyWorker(sourceSymbol, immediateTarget, overwriteEmpty) ||
            MarkSymbolOfAliasDeclarationIfTypeOnlyWorker(sourceSymbol, finalTarget, overwriteEmpty);
    }

    private bool MarkSymbolOfAliasDeclarationIfTypeOnlyWorker(SymbolRef aliasSymbol, SymbolRef target, bool overwriteEmpty)
    {
        var state = AliasSymbolLinks.Get(aliasSymbol).TypeOnlyState;
        if (!target.IsNone && (state == 0 || (overwriteEmpty && state == 1)))
        {
            var exportSymbol = ExportsOf(target)?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
            if (exportSymbol.IsNone)
            {
                exportSymbol = target;
            }
            var typeOnly = (DeclarationsOf(exportSymbol) ?? []).Find(IsTypeOnlyImportOrExportDeclaration);
            if (typeOnly.IsNone && (SymbolFlagsOf(exportSymbol) & SymbolFlags.Alias) != 0 && AliasSymbolLinks.Get(exportSymbol).TypeOnlyState == 2)
            {
                typeOnly = AliasSymbolLinks.Get(exportSymbol).TypeOnlyDeclaration;
            }
            ref var links = ref AliasSymbolLinks.Get(aliasSymbol);
            links.TypeOnlyDeclaration = typeOnly;
            links.TypeOnlyState = typeOnly.IsNone ? (byte)1 : (byte)2;
        }
        return AliasSymbolLinks.Get(aliasSymbol).TypeOnlyState == 2;
    }

    /// <summary>utilities.ts isTypeOnlyImportOrExportDeclaration.</summary>
    internal bool IsTypeOnlyImportOrExportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.ImportSpecifier:
                return store.IsTypeOnly(n) || store.IsTypeOnly(store.Parent(store.Parent(n)));
            case SyntaxKind.ExportSpecifier:
                return store.IsTypeOnly(n) || store.IsTypeOnly(store.Parent(store.Parent(n)));
            case SyntaxKind.NamespaceImport:
                return store.IsTypeOnly(store.Parent(n));
            case SyntaxKind.ImportClause:
            case SyntaxKind.ImportEqualsDeclaration:
                return store.IsTypeOnly(n);
            case SyntaxKind.NamespaceExport:
                return store.IsTypeOnly(store.Parent(n));
            case SyntaxKind.ExportDeclaration:
                return store.IsTypeOnly(n) && !store.ModuleSpecifier(n).IsNone && store.ExportClause(n).IsNone;
            default:
                return false;
        }
    }

    private void CheckResolvedBlockScopedVariable(SymbolRef result, NodeRef errorLocation)
    {
        var flags = SymbolFlagsOf(result);
        if ((flags & (SymbolFlags.Function | SymbolFlags.FunctionScopedVariable | SymbolFlags.Assignment)) != 0 && (flags & SymbolFlags.Class) != 0)
        {
            // constructor functions aren't block scoped
            return;
        }
        // Block-scoped variables cannot be used before their definition
        var declaration = NodeRef.None;
        foreach (var d in DeclarationsOf(result) ?? [])
        {
            var store = StoreOf(d);
            if (Utilities.IsBlockOrCatchScoped(store, d.Node) || Utilities.IsClassLike(store, d.Node) || store.Kind(d.Node) == SyntaxKind.EnumDeclaration)
            {
                declaration = d;
                break;
            }
        }
        if (declaration.IsNone)
        {
            return;
        }
        var declarationStore = StoreOf(declaration);
        if ((declarationStore.Flags(declaration.Node) & NodeFlags.Ambient) == 0 && !IsBlockScopedNameDeclaredBeforeUse(declaration, errorLocation))
        {
            ProgramDiagnostic? diagnosticMessage = null;
            var declarationName = Utilities.DeclarationNameToString(declarationStore, Utilities.GetNameOfDeclaration(declarationStore, declaration.Node));
            if ((flags & SymbolFlags.BlockScopedVariable) != 0)
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Block_scoped_variable_0_used_before_its_declaration, declarationName);
            }
            else if ((flags & SymbolFlags.Class) != 0)
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Class_0_used_before_its_declaration, declarationName);
            }
            else if ((flags & SymbolFlags.RegularEnum) != 0)
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Enum_0_used_before_its_declaration, declarationName);
            }
            else if (compilerOptions.GetIsolatedModules())
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Enum_0_used_before_its_declaration, declarationName);
            }
            if (diagnosticMessage is not null)
            {
                (diagnosticMessage.RelatedInformation ??= []).Add(CreateRelatedForNode(declaration, Diagnostics._0_is_declared_here, declarationName));
            }
        }
    }

    /// <summary>checker.ts isBlockScopedNameDeclaredBeforeUse.</summary>
    private bool IsBlockScopedNameDeclaredBeforeUse(NodeRef declaration, NodeRef usage)
    {
        var store = StoreOf(declaration);
        var declContainer = At(declaration, Utilities.GetEnclosingBlockScopeContainer(store, declaration.Node));
        if (declaration.File != usage.File)
        {
            // nodes are in different files and order cannot be determined (outFile is not supported by Track O's programs)
            return true;
        }
        var u = usage.Node;
        var d = declaration.Node;

        // deferred usage in a type context is always OK regardless of the usage position:
        if ((store.Flags(u) & NodeFlags.JSDoc) != 0 || Utilities.IsInTypeQuery(store, u) || IsInAmbientOrTypeNode(store, u))
        {
            return true;
        }

        var declKind = store.Kind(d);
        if (store.Pos(d) <= store.Pos(u) && !(declKind == SyntaxKind.PropertyDeclaration && IsThisProperty(store, store.Parent(u)) && store.Initializer(d).IsNone && store.ExclamationToken(d).IsNone))
        {
            // declaration is before usage
            if (declKind == SyntaxKind.BindingElement)
            {
                // still might be illegal if declaration and usage are both binding elements (eg var [a = b, b = b] = [1, 2])
                var errorBindingElement = GetAncestor(store, u, SyntaxKind.BindingElement);
                if (!errorBindingElement.IsNone)
                {
                    return GetAncestor(store, errorBindingElement, SyntaxKind.BindingElement) != GetAncestor(store, d, SyntaxKind.BindingElement) ||
                        store.Pos(d) < store.Pos(errorBindingElement);
                }
                // or it might be illegal if usage happens before parent variable is declared (eg var [a] = a)
                return IsBlockScopedNameDeclaredBeforeUse(At(declaration, GetAncestor(store, d, SyntaxKind.VariableDeclaration)), usage);
            }
            if (declKind == SyntaxKind.VariableDeclaration)
            {
                // still might be illegal if usage is in the initializer of the variable declaration (eg var a = a)
                return !IsImmediatelyUsedInInitializerOfBlockScopedVariable(store, d, u, declContainer.Node);
            }
            if (Utilities.IsClassLike(store, d))
            {
                // still might be illegal if the usage is within a computed property name in the class (eg class A { static p = "a"; [A.p]() {} })
                // or when used within a decorator in the class
                var container = Node.None;
                for (var n = u; !n.IsNone; n = store.Parent(n))
                {
                    if (n == d)
                    {
                        break;
                    }
                    var kind = store.Kind(n);
                    if (kind == SyntaxKind.ComputedPropertyName)
                    {
                        if (store.Parent(store.Parent(n)) == d)
                        {
                            container = n;
                            break;
                        }
                        continue;
                    }
                    if (kind == SyntaxKind.Decorator && compilerOptions.ExperimentalDecorators != true)
                    {
                        var p = store.Parent(n);
                        var pKind = store.Kind(p);
                        if (p == d || ((pKind is SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.PropertyDeclaration) && store.Parent(p) == d)
                            || (pKind == SyntaxKind.Parameter && store.Parent(store.Parent(p)) == d))
                        {
                            container = n;
                            break;
                        }
                    }
                }
                if (container.IsNone)
                {
                    return true;
                }
                if (compilerOptions.ExperimentalDecorators != true && store.Kind(container) == SyntaxKind.Decorator)
                {
                    for (var n = u; !n.IsNone; n = store.Parent(n))
                    {
                        if (n == container)
                        {
                            return false;
                        }
                        if (Utilities.IsFunctionLike(store, n) && Utilities.GetImmediatelyInvokedFunctionExpression(store, n).IsNone)
                        {
                            return true;
                        }
                    }
                    return false;
                }
                return false;
            }
            if (declKind == SyntaxKind.PropertyDeclaration)
            {
                // still might be illegal if a self-referencing property initializer (eg private x = this.x)
                return !IsPropertyImmediatelyReferencedWithinDeclaration(store, d, u, stopAtAnyPropertyDeclaration: false);
            }
            if (Utilities.IsParameterPropertyDeclaration(store, d, store.Parent(d)))
            {
                // foo = this.bar is illegal in emitStandardClassFields when bar is a parameter property
                return !(EmitStandardClassFields
                    && Utilities.GetContainingClass(store, d) == Utilities.GetContainingClass(store, u)
                    && IsUsedInFunctionOrInstanceProperty(store, usage.File, u, d, declContainer.Node));
            }
            return true;
        }

        // declaration is after usage, but it can still be legal if usage is deferred:
        var usageParent = store.Parent(u);
        if (store.Kind(usageParent) == SyntaxKind.ExportSpecifier || (store.Kind(usageParent) == SyntaxKind.ExportAssignment && store.IsExportEquals(usageParent)))
        {
            // export specifiers do not use the variable, they only make it available for use
            return true;
        }
        // When resolving symbols for exports, the `usage` location passed in can be the export site directly
        if (store.Kind(u) == SyntaxKind.ExportAssignment && store.IsExportEquals(u))
        {
            return true;
        }
        if (IsUsedInFunctionOrInstanceProperty(store, usage.File, u, d, declContainer.Node))
        {
            if (EmitStandardClassFields && !Utilities.GetContainingClass(store, d).IsNone
                && (declKind == SyntaxKind.PropertyDeclaration || Utilities.IsParameterPropertyDeclaration(store, d, store.Parent(d))))
            {
                return !IsPropertyImmediatelyReferencedWithinDeclaration(store, d, u, stopAtAnyPropertyDeclaration: true);
            }
            return true;
        }
        return false;
    }

    private static Node GetAncestor(NodeStore store, Node node, SyntaxKind kind)
    {
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) == kind)
            {
                return n;
            }
        }
        return Node.None;
    }

    // utilities.ts isThisProperty
    private static bool IsThisProperty(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression && store.Kind(store.Expression(node)) == SyntaxKind.ThisKeyword;

    private static bool IsInAmbientOrTypeNode(NodeStore store, Node node)
    {
        if ((store.Flags(node) & NodeFlags.Ambient) != 0)
        {
            return true;
        }
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) is SyntaxKind.InterfaceDeclaration or SyntaxKind.TypeAliasDeclaration or SyntaxKind.TypeLiteral)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsImmediatelyUsedInInitializerOfBlockScopedVariable(NodeStore store, Node declaration, Node usage, Node declContainer)
    {
        var grandparent = store.Parent(store.Parent(declaration));
        switch (store.Kind(grandparent))
        {
            case SyntaxKind.VariableStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForOfStatement:
                // variable statement/for/for-of statement case,
                // use site should not be inside variable declaration (initializer of declaration or binding element)
                if (IsSameScopeDescendentOf(store, usage, declaration, declContainer))
                {
                    return true;
                }
                break;
        }
        // ForIn/ForOf case - use site should not be used in expression part
        return store.Kind(grandparent) is SyntaxKind.ForInStatement or SyntaxKind.ForOfStatement && IsSameScopeDescendentOf(store, usage, store.Expression(grandparent), declContainer);
    }

    // checker.ts isSameScopeDescendentOf
    private static bool IsSameScopeDescendentOf(NodeStore store, Node initial, Node parent, Node stopAt)
    {
        if (parent.IsNone)
        {
            return false;
        }
        for (var n = initial; !n.IsNone; n = store.Parent(n))
        {
            if (n == parent)
            {
                return true;
            }
            if (n == stopAt || (Utilities.IsFunctionLike(store, n) && (Utilities.GetImmediatelyInvokedFunctionExpression(store, n).IsNone || IsAsyncGenerator(store, n))))
            {
                return false;
            }
        }
        return false;
    }

    // getFunctionFlags(n) & FunctionFlags.AsyncGenerator
    private static bool IsAsyncGenerator(NodeStore store, Node node) =>
        !store.AsteriskToken(node).IsNone || Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Async);

    private bool IsUsedInFunctionOrInstanceProperty(NodeStore store, int file, Node usage, Node declaration, Node declContainer)
    {
        for (var current = usage; !current.IsNone; current = store.Parent(current))
        {
            if (current == declContainer)
            {
                return false;
            }
            if (Utilities.IsFunctionLike(store, current))
            {
                if (Utilities.GetImmediatelyInvokedFunctionExpression(store, current).IsNone)
                {
                    return true;
                }
                continue;
            }
            if (store.Kind(current) == SyntaxKind.ClassStaticBlockDeclaration)
            {
                if (store.Pos(declaration) < store.Pos(usage))
                {
                    return true;
                }
                continue;
            }
            var parent = store.Parent(current);
            if (!parent.IsNone && store.Kind(parent) == SyntaxKind.PropertyDeclaration && store.Initializer(parent) == current)
            {
                if (Utilities.IsStatic(store, parent))
                {
                    if (store.Kind(declaration) == SyntaxKind.MethodDeclaration)
                    {
                        return true;
                    }
                    if (store.Kind(declaration) == SyntaxKind.PropertyDeclaration && Utilities.GetContainingClass(store, usage) == Utilities.GetContainingClass(store, declaration))
                    {
                        var propName = store.Name(declaration);
                        if (store.Kind(propName) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier)
                        {
                            var declarationRef = new NodeRef(file, declaration);
                            var type = GetTypeOfSymbol(GetSymbolOfDeclaration(declarationRef));
                            var staticBlocks = new List<NodeRef>();
                            var classNode = store.Parent(declaration);
                            foreach (var member in store.Nodes(store.Members(classNode)))
                            {
                                if (store.Kind(member) == SyntaxKind.ClassStaticBlockDeclaration)
                                {
                                    staticBlocks.Add(new NodeRef(file, member));
                                }
                            }
                            if (IsPropertyInitializedInStaticBlocks(new NodeRef(file, propName), type, staticBlocks, store.Pos(classNode), store.Pos(current)))
                            {
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    var isDeclarationInstanceProperty = store.Kind(declaration) == SyntaxKind.PropertyDeclaration && !Utilities.IsStatic(store, declaration);
                    if (!isDeclarationInstanceProperty || Utilities.GetContainingClass(store, usage) != Utilities.GetContainingClass(store, declaration))
                    {
                        return true;
                    }
                }
            }
            if (!parent.IsNone && store.Kind(parent) == SyntaxKind.Decorator && store.Expression(parent) == current)
            {
                var decorated = store.Parent(parent);
                if (store.Kind(decorated) == SyntaxKind.Parameter)
                {
                    return IsUsedInFunctionOrInstanceProperty(store, file, store.Parent(store.Parent(decorated)), declaration, declContainer);
                }
                if (store.Kind(decorated) == SyntaxKind.MethodDeclaration)
                {
                    return IsUsedInFunctionOrInstanceProperty(store, file, store.Parent(decorated), declaration, declContainer);
                }
            }
        }
        return false;
    }

    private static bool IsPropertyImmediatelyReferencedWithinDeclaration(NodeStore store, Node declaration, Node usage, bool stopAtAnyPropertyDeclaration)
    {
        // always legal if usage is after declaration
        if (store.End(usage) > store.End(declaration))
        {
            return false;
        }
        // still might be legal if usage is deferred (e.g. x: any = () => this.x)
        // otherwise illegal if immediately referenced within the declaration (e.g. x: any = this.x)
        for (var node = usage; !node.IsNone; node = store.Parent(node))
        {
            if (node == declaration)
            {
                return true; // ancestorChangingReferenceScope === undefined
            }
            switch (store.Kind(node))
            {
                case SyntaxKind.ArrowFunction:
                    return false;
                case SyntaxKind.PropertyDeclaration:
                {
                    // even when stopping at any property declaration, they need to come from the same class
                    var quit = stopAtAnyPropertyDeclaration &&
                        ((store.Kind(declaration) == SyntaxKind.PropertyDeclaration && store.Parent(node) == store.Parent(declaration))
                            || (Utilities.IsParameterPropertyDeclaration(store, declaration, store.Parent(declaration)) && store.Parent(node) == store.Parent(store.Parent(declaration))));
                    if (quit)
                    {
                        return true;
                    }
                    return false;
                }
                case SyntaxKind.Block:
                    switch (store.Kind(store.Parent(node)))
                    {
                        case SyntaxKind.GetAccessor:
                        case SyntaxKind.MethodDeclaration:
                        case SyntaxKind.SetAccessor:
                            return false;
                    }
                    break;
            }
        }
        return true;
    }

    // ---- checkAndReportErrorFor* ----

    private bool CheckAndReportErrorForMissingPrefix(NodeRef errorLocation, string name, NodeRef nameNode)
    {
        var store = StoreOf(errorLocation);
        var node = errorLocation.Node;
        if (store.Kind(node) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(node), name) ||
            Utilities.IsTypeReferenceIdentifier(store, node) || Utilities.IsInTypeQuery(store, node))
        {
            return false;
        }
        var container = Utilities.GetThisContainer(store, node, includeArrowFunctions: false, includeClassComputedPropertyName: false);
        for (var location = container; !location.IsNone; location = store.Parent(location))
        {
            var parent = store.Parent(location);
            if (!parent.IsNone && Utilities.IsClassLike(store, parent))
            {
                var classSymbol = GetSymbolOfDeclaration(At(errorLocation, parent));
                if (classSymbol.IsNone)
                {
                    break;
                }
                // Check to see if a static member exists.
                var constructorType = GetTypeOfSymbol(classSymbol);
                if (!GetPropertyOfType(constructorType, name).IsNone)
                {
                    Error(errorLocation, Diagnostics.Cannot_find_name_0_Did_you_mean_the_static_member_1_0, DiagnosticName(name, nameNode), SymbolToString(classSymbol));
                    return true;
                }
                // No static member is present.
                // Check if we're in an instance method and look for a relevant instance member.
                if (location == container && !Utilities.IsStatic(store, location))
                {
                    var instanceType = ThisTypeOfClassOrInterface(GetDeclaredTypeOfSymbol(classSymbol));
                    if (!instanceType.IsNone && !GetPropertyOfType(instanceType, name).IsNone)
                    {
                        Error(errorLocation, Diagnostics.Cannot_find_name_0_Did_you_mean_the_instance_member_this_0, DiagnosticName(name, nameNode));
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private bool CheckAndReportErrorForExtendingInterface(NodeRef errorLocation)
    {
        var expression = GetEntityNameForExtendingInterface(errorLocation);
        if (!expression.IsNone && !ResolveEntityName(expression, SymbolFlags.Interface, ignoreErrors: true).IsNone)
        {
            var store = StoreOf(expression);
            Error(errorLocation, Diagnostics.Cannot_extend_an_interface_0_Did_you_mean_implements, Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, expression.Node), expression.Node));
            return true;
        }
        return false;
    }

    private NodeRef GetEntityNameForExtendingInterface(NodeRef node)
    {
        while (true)
        {
            switch (Kind(node))
            {
                case SyntaxKind.Identifier:
                case SyntaxKind.PropertyAccessExpression:
                    node = Parent(node);
                    if (node.IsNone)
                    {
                        return NodeRef.None;
                    }
                    continue;
                case SyntaxKind.ExpressionWithTypeArguments:
                {
                    var store = StoreOf(node);
                    var expression = store.Expression(node.Node);
                    if (Utilities.IsEntityNameExpression(store, expression))
                    {
                        return At(node, expression);
                    }
                    return NodeRef.None;
                }
                default:
                    return NodeRef.None;
            }
        }
    }

    private bool CheckAndReportErrorForUsingTypeAsNamespace(NodeRef errorLocation, string name, SymbolFlags meaning)
    {
        var namespaceMeaning = SymbolFlags.Namespace | (Utilities.IsInJSFile(StoreOf(errorLocation), errorLocation.Node) ? SymbolFlags.Value : 0);
        if (meaning == namespaceMeaning)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, NodeRef.None, SymbolFlags.Type & ~namespaceMeaning, null, isUse: false));
            var parent = Parent(errorLocation);
            if (!symbol.IsNone)
            {
                if (Kind(parent) == SyntaxKind.QualifiedName)
                {
                    var store = StoreOf(parent);
                    var propName = store.Texts.GetString(store.EscapedText(store.Right(parent.Node)))!;
                    var propType = GetPropertyOfType(GetDeclaredTypeOfSymbol(symbol), propName);
                    if (!propType.IsNone)
                    {
                        Error(parent, Diagnostics.Cannot_access_0_1_because_0_is_a_type_but_not_a_namespace_Did_you_mean_to_retrieve_the_type_of_the_property_1_in_0_with_0_1,
                            Utilities.UnescapeLeadingUnderscores(name), Utilities.UnescapeLeadingUnderscores(propName));
                        return true;
                    }
                }
                Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_namespace_here, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }
        return false;
    }

    private bool CheckAndReportErrorForUsingValueAsType(NodeRef errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & (SymbolFlags.Type & ~SymbolFlags.Namespace)) != 0)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, NodeRef.None, ~SymbolFlags.Type & SymbolFlags.Value, null, isUse: false));
            if (!symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.Namespace) == 0)
            {
                Error(errorLocation, Diagnostics._0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }
        return false;
    }

    private static bool IsPrimitiveTypeName(string name) => name is "any" or "string" or "number" or "boolean" or "never" or "unknown";

    private bool CheckAndReportErrorForExportingPrimitiveType(NodeRef errorLocation, string name)
    {
        if (IsPrimitiveTypeName(name) && Kind(Parent(errorLocation)) == SyntaxKind.ExportSpecifier)
        {
            Error(errorLocation, Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, name);
            return true;
        }
        return false;
    }

    private bool CheckAndReportErrorForUsingTypeAsValue(NodeRef errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & SymbolFlags.Value) != 0)
        {
            if (IsPrimitiveTypeName(name))
            {
                var grandparent = Parent(Parent(errorLocation));
                if (!grandparent.IsNone && !Parent(grandparent).IsNone && Kind(grandparent) == SyntaxKind.HeritageClause)
                {
                    var store = StoreOf(grandparent);
                    var heritageKind = store.Token(grandparent.Node);
                    var container = store.Parent(grandparent.Node);
                    var containerKind = store.Kind(container);
                    if (containerKind == SyntaxKind.InterfaceDeclaration && heritageKind == SyntaxKind.ExtendsKeyword)
                    {
                        Error(errorLocation, Diagnostics.An_interface_cannot_extend_a_primitive_type_like_0_It_can_only_extend_other_named_object_types, Utilities.UnescapeLeadingUnderscores(name));
                    }
                    else if (Utilities.IsClassLike(store, container) && heritageKind == SyntaxKind.ExtendsKeyword)
                    {
                        Error(errorLocation, Diagnostics.A_class_cannot_extend_a_primitive_type_like_0_Classes_can_only_extend_constructable_values, Utilities.UnescapeLeadingUnderscores(name));
                    }
                    else if (Utilities.IsClassLike(store, container) && heritageKind == SyntaxKind.ImplementsKeyword)
                    {
                        Error(errorLocation, Diagnostics.A_class_cannot_implement_a_primitive_type_like_0_It_can_only_implement_other_named_object_types, Utilities.UnescapeLeadingUnderscores(name));
                    }
                }
                else
                {
                    Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here, Utilities.UnescapeLeadingUnderscores(name));
                }
                return true;
            }
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, NodeRef.None, SymbolFlags.Type & ~SymbolFlags.Value, null, isUse: false));
            if (!symbol.IsNone)
            {
                var allFlags = GetSymbolFlags(symbol);
                if ((allFlags & SymbolFlags.Value) == 0)
                {
                    var rawName = Utilities.UnescapeLeadingUnderscores(name);
                    if (name is "Promise" or "Symbol" or "Map" or "WeakMap" or "Set" or "WeakSet")
                    {
                        Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_es2015_or_later, rawName);
                    }
                    else if (MaybeMappedType(errorLocation, symbol))
                    {
                        Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here_Did_you_mean_to_use_1_in_0, rawName, rawName == "K" ? "P" : "K");
                    }
                    else
                    {
                        Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here, rawName);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    // checker.ts maybeMappedType (allTypesAssignableToKind on a union of string/number literal types).
    private bool MaybeMappedType(NodeRef node, SymbolRef symbol)
    {
        var store = StoreOf(node);
        var container = Node.None;
        for (var n = store.Parent(node.Node); !n.IsNone; n = store.Parent(n))
        {
            var kind = store.Kind(n);
            if (kind is SyntaxKind.ComputedPropertyName or SyntaxKind.PropertySignature)
            {
                continue;
            }
            if (kind == SyntaxKind.TypeLiteral)
            {
                container = n;
            }
            break;
        }
        if (!container.IsNone && store.Length(store.Members(container)) == 1)
        {
            var type = GetDeclaredTypeOfSymbol(symbol);
            if ((Types.Flags(type) & TypeFlags.Union) != 0)
            {
                foreach (var t in Types.Elements(Types.Union(type).Types))
                {
                    if ((Types.Flags(t) & TypeFlags.StringOrNumberLiteral) == 0)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        return false;
    }

    private bool CheckAndReportErrorForUsingNamespaceAsTypeOrValue(NodeRef errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & (SymbolFlags.Value & ~SymbolFlags.Type)) != 0)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, NodeRef.None, SymbolFlags.NamespaceModule, null, isUse: false));
            if (!symbol.IsNone)
            {
                Error(errorLocation, Diagnostics.Cannot_use_namespace_0_as_a_value, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }
        else if ((meaning & (SymbolFlags.Type & ~SymbolFlags.Value)) != 0)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, NodeRef.None, SymbolFlags.Module, null, isUse: false));
            if (!symbol.IsNone)
            {
                Error(errorLocation, Diagnostics.Cannot_use_namespace_0_as_a_type, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }
        return false;
    }

    // ---- suggestions ----

    // utilities.ts getScriptTargetFeatures: the feature names and the first library that has them.
    private static readonly Dictionary<string, string> ScriptTargetFeatureLibs = new(StringComparer.Ordinal)
    {
        ["Array"] = "es2015", ["Iterator"] = "es2015", ["AsyncIterator"] = "es2015", ["ArrayBuffer"] = "es2024", ["Atomics"] = "es2017",
        ["SharedArrayBuffer"] = "es2017", ["AsyncIterable"] = "es2018", ["AsyncIterableIterator"] = "es2018", ["AsyncGenerator"] = "es2018",
        ["AsyncGeneratorFunction"] = "es2018", ["RegExp"] = "es2015", ["RegExpConstructor"] = "es2025", ["Reflect"] = "es2015",
        ["ArrayConstructor"] = "es2015", ["ObjectConstructor"] = "es2015", ["NumberConstructor"] = "es2015", ["Math"] = "es2015",
        ["Map"] = "es2015", ["MapConstructor"] = "es2024", ["Set"] = "es2015", ["PromiseConstructor"] = "es2015", ["Symbol"] = "es2015",
        ["WeakMap"] = "es2015", ["WeakSet"] = "es2015", ["String"] = "es2015", ["StringConstructor"] = "es2015", ["DateTimeFormat"] = "es2017",
        ["Promise"] = "es2015", ["RegExpMatchArray"] = "es2018", ["RegExpExecArray"] = "es2018", ["Intl"] = "es2018", ["NumberFormat"] = "es2018",
        ["SymbolConstructor"] = "es2020", ["DataView"] = "es2020", ["BigInt"] = "es2020", ["RelativeTimeFormat"] = "es2020",
        ["Int8Array"] = "es2022", ["Uint8Array"] = "es2022", ["Uint8ClampedArray"] = "es2022", ["Int16Array"] = "es2022",
        ["Uint16Array"] = "es2022", ["Int32Array"] = "es2022", ["Uint32Array"] = "es2022", ["Float16Array"] = "es2025",
        ["Float32Array"] = "es2022", ["Float64Array"] = "es2022", ["BigInt64Array"] = "es2020", ["BigUint64Array"] = "es2020",
        ["Error"] = "es2022", ["ErrorConstructor"] = "esnext", ["Uint8ArrayConstructor"] = "esnext", ["Date"] = "esnext",
        ["DisposableStack"] = "esnext", ["AsyncDisposableStack"] = "esnext",
    };

    private static string? GetSuggestedLibForNonExistentName(string missingName) =>
        ScriptTargetFeatureLibs.TryGetValue(missingName, out var lib) ? lib : null;

    private SymbolRef GetSuggestedSymbolForNonexistentSymbol(NodeRef location, string outerName, SymbolFlags meaning) =>
        ResolveNameHelper(location, outerName, NodeRef.None, meaning, null, isUse: false, excludeGlobals: false, forSuggestion: true);

    private SymbolRef GetSuggestionForSymbolNameLookup(SymTable symbols, string name, SymbolFlags meaning)
    {
        var symbol = GetSymbol(symbols, name, meaning);
        // Sometimes the symbol is found when location is a return type of a function: `typeof x` and `x` is declared in the body of the function
        // So the table *contains* `x` but `x` isn't actually in scope.
        // However, resolveNameHelper will continue and call this callback again, so we'll eventually get a correct suggestion.
        if (!symbol.IsNone)
        {
            return symbol;
        }
        var candidates = new List<SymbolRef>(symbols.Count + 6);
        if (symbols == globals)
        {
            foreach (var s in (string[])["string", "number", "boolean", "object", "bigint", "symbol"])
            {
                if (symbols.Has(char.ToUpperInvariant(s[0]) + s[1..]))
                {
                    candidates.Add(CreateSymbol(SymbolFlags.TypeAlias, s));
                }
            }
        }
        for (var i = 0; i < symbols.Count; i++)
        {
            candidates.Add(symbols.ValueAt(i));
        }
        return GetSpellingSuggestionForName(Utilities.UnescapeLeadingUnderscores(name), candidates, meaning);
    }

    /// <summary>checker.ts getSpellingSuggestionForName (core.ts getSpellingSuggestion).</summary>
    internal SymbolRef GetSpellingSuggestionForName(string name, List<SymbolRef> symbols, SymbolFlags meaning)
    {
        var maximumLengthDifference = Math.Max(2, (int)Math.Floor(name.Length * 0.34));
        var bestDistance = Math.Floor(name.Length * 0.4) + 1; // If the best result is worse than this, don't bother.
        var bestCandidate = SymbolRef.None;
        foreach (var candidate in symbols)
        {
            var candidateName = GetCandidateName(candidate, meaning);
            if (candidateName is not null && Math.Abs(candidateName.Length - name.Length) <= maximumLengthDifference)
            {
                if (candidateName == name)
                {
                    continue;
                }
                // Only consider candidates less than 3 characters long when they differ by case.
                if (candidateName.Length < 3 && !string.Equals(candidateName.ToLowerInvariant(), name.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    continue;
                }
                var distance = LevenshteinWithMax(name, candidateName, bestDistance - 0.1);
                if (distance is null)
                {
                    continue;
                }
                bestDistance = distance.Value;
                bestCandidate = candidate;
            }
        }
        return bestCandidate;
    }

    private string? GetCandidateName(SymbolRef candidate, SymbolFlags meaning)
    {
        var candidateName = Utilities.UnescapeLeadingUnderscores(NameOf(candidate)); // symbolName
        if (candidateName.StartsWith('"'))
        {
            return null;
        }
        var flags = SymbolFlagsOf(candidate);
        if ((flags & meaning) != 0)
        {
            return candidateName;
        }
        if ((flags & SymbolFlags.Alias) != 0)
        {
            var alias = TryResolveAlias(candidate);
            if (!alias.IsNone && (SymbolFlagsOf(alias) & meaning) != 0)
            {
                return candidateName;
            }
        }
        return null;
    }

    private static double? LevenshteinWithMax(string s1, string s2, double max)
    {
        var previous = new double[s2.Length + 1];
        var current = new double[s2.Length + 1];
        // Represents any value > max. We don't care about the particular value.
        var big = max + 0.01;
        for (var i = 0; i <= s2.Length; i++)
        {
            previous[i] = i;
        }
        for (var i = 1; i <= s1.Length; i++)
        {
            var c1 = s1[i - 1];
            var minJ = (int)Math.Ceiling(i > max ? i - max : 1);
            var maxJ = (int)Math.Floor(s2.Length > max + i ? max + i : s2.Length);
            current[0] = i;
            // Smallest value of the matrix in the ith column.
            double colMin = i;
            for (var j = 1; j < minJ; j++)
            {
                current[j] = big;
            }
            for (var j = minJ; j <= maxJ; j++)
            {
                // case difference should be significantly cheaper than other differences
                var substitutionDistance = char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1])
                    ? previous[j - 1] + 0.1
                    : previous[j - 1] + 2;
                var dist = c1 == s2[j - 1]
                    ? previous[j - 1]
                    : Math.Min(Math.Min(/*delete*/ previous[j] + 1, /*insert*/ current[j - 1] + 1), /*substitute*/ substitutionDistance);
                current[j] = dist;
                colMin = Math.Min(colMin, dist);
            }
            for (var j = maxJ + 1; j <= s2.Length; j++)
            {
                current[j] = big;
            }
            if (colMin > max)
            {
                // Give up -- everything in this column is > max and it can't get better in future columns.
                return null;
            }
            (previous, current) = (current, previous);
        }
        var res = previous[s2.Length];
        return res > max ? null : res;
    }

    // ---- identifiers ----

    /// <summary>checker.ts getResolvedSymbol: the symbol an identifier in an expression refers to (unknownSymbol if none).</summary>
    internal SymbolRef GetResolvedSymbol(NodeRef node)
    {
        ref var links = ref SymbolNodeLinks.Get(node);
        if (links.ResolvedSymbol.IsNone)
        {
            var store = StoreOf(node);
            var resolved = SymbolRef.None;
            if (!Utilities.NodeIsMissing(store, node.Node))
            {
                resolved = ResolveName(node, store.Texts.GetString(store.EscapedText(node.Node))!, node,
                    SymbolFlags.Value | SymbolFlags.ExportValue, GetCannotFindNameDiagnosticForName(node), !Utilities.IsWriteOnlyAccess(store, node.Node));
            }
            SymbolNodeLinks.Get(node).ResolvedSymbol = resolved.IsNone ? unknownSymbol : resolved;
        }
        return SymbolNodeLinks.Get(node).ResolvedSymbol;
    }

    /// <summary>checker.ts getCannotFindNameDiagnosticForName (types: [] in Gate C, so usesWildcardTypes is false).</summary>
    internal DiagnosticMessage GetCannotFindNameDiagnosticForName(NodeRef node)
    {
        var store = StoreOf(node);
        var text = store.Texts.GetString(store.EscapedText(node.Node));
        switch (text)
        {
            case "document":
            case "console":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_include_dom;
            case "$":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery_and_then_add_jquery_to_the_types_field_in_your_tsconfig;
            case "beforeEach":
            case "describe":
            case "suite":
            case "it":
            case "test":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha_and_then_add_jest_or_mocha_to_the_types_field_in_your_tsconfig;
            case "process":
            case "require":
            case "Buffer":
            case "module":
            case "NodeJS":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig;
            case "Bun":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_Bun_Try_npm_i_save_dev_types_Slashbun_and_then_add_bun_to_the_types_field_in_your_tsconfig;
            case "Map":
            case "Set":
            case "Promise":
            case "Symbol":
            case "WeakMap":
            case "WeakSet":
            case "Iterator":
            case "AsyncIterator":
            case "SharedArrayBuffer":
            case "Atomics":
            case "AsyncIterable":
            case "AsyncIterableIterator":
            case "AsyncGenerator":
            case "AsyncGeneratorFunction":
            case "BigInt":
            case "Reflect":
            case "BigInt64Array":
            case "BigUint64Array":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_1_or_later;
            case "await":
                if (store.Kind(store.Parent(node.Node)) == SyntaxKind.CallExpression)
                {
                    return Diagnostics.Cannot_find_name_0_Did_you_mean_to_write_this_in_an_async_function;
                }
                goto default;
            default:
                if (store.Kind(store.Parent(node.Node)) == SyntaxKind.ShorthandPropertyAssignment)
                {
                    return Diagnostics.No_value_exists_in_scope_for_the_shorthand_property_0_Either_declare_one_or_provide_an_initializer;
                }
                return Diagnostics.Cannot_find_name_0;
        }
    }

    // ---- entity names and aliases ----

    /// <summary>checker.ts resolveEntityName: resolves a qualified name and any involved aliases.</summary>
    internal SymbolRef ResolveEntityName(NodeRef name, SymbolFlags meaning, bool ignoreErrors = false, bool dontResolveAlias = false, NodeRef location = default)
    {
        var store = StoreOf(name);
        if (Utilities.NodeIsMissing(store, name.Node))
        {
            return SymbolRef.None;
        }
        var namespaceMeaning = SymbolFlags.Namespace | (Utilities.IsInJSFile(store, name.Node) ? meaning & SymbolFlags.Value : 0);
        SymbolRef symbol;
        var kind = store.Kind(name.Node);
        if (kind == SyntaxKind.Identifier)
        {
            var message = meaning == namespaceMeaning ? Diagnostics.Cannot_find_namespace_0 : GetCannotFindNameDiagnosticForName(At(name, Utilities.GetFirstIdentifier(store, name.Node)));
            // (resolveEntityNameFromAssignmentDeclaration: JavaScript, module C9)
            symbol = GetMergedSymbol(ResolveName(location.IsNone ? name : location, store.Texts.GetString(store.EscapedText(name.Node))!, name, meaning, ignoreErrors ? null : message, isUse: true));
            if (symbol.IsNone)
            {
                return SymbolRef.None;
            }
        }
        else if (kind is SyntaxKind.QualifiedName or SyntaxKind.PropertyAccessExpression)
        {
            var left = At(name, kind == SyntaxKind.QualifiedName ? store.Left(name.Node) : store.Expression(name.Node));
            var right = At(name, kind == SyntaxKind.QualifiedName ? store.Right(name.Node) : store.Name(name.Node));
            var ns = ResolveEntityName(left, namespaceMeaning, ignoreErrors, dontResolveAlias: false, location);
            if (ns.IsNone || Utilities.NodeIsMissing(store, right.Node))
            {
                return SymbolRef.None;
            }
            if (ns == unknownSymbol)
            {
                return ns;
            }
            // (CommonJS require namespaces: module C9)
            var rightName = store.Texts.GetString(store.EscapedText(right.Node))!;
            symbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(ns), rightName, meaning));
            if (symbol.IsNone && (SymbolFlagsOf(ns) & SymbolFlags.Alias) != 0)
            {
                // `namespace` can be resolved further if there was a symbol merge with a re-export
                symbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(ResolveAlias(ns)), rightName, meaning));
            }
            if (symbol.IsNone)
            {
                if (!ignoreErrors)
                {
                    var namespaceName = GetFullyQualifiedName(ns);
                    var declarationName = Utilities.DeclarationNameToString(store, right.Node);
                    var suggestionForNonexistentModule = GetSuggestedSymbolForNonexistentModule(right, ns);
                    if (!suggestionForNonexistentModule.IsNone)
                    {
                        Error(right, Diagnostics._0_has_no_exported_member_named_1_Did_you_mean_2, namespaceName, declarationName, SymbolToString(suggestionForNonexistentModule));
                        return SymbolRef.None;
                    }
                    var containingQualifiedName = NodeRef.None;
                    if (kind == SyntaxKind.QualifiedName)
                    {
                        var c = name.Node;
                        while (store.Kind(store.Parent(c)) == SyntaxKind.QualifiedName)
                        {
                            c = store.Parent(c);
                        }
                        containingQualifiedName = At(name, c);
                    }
                    var canSuggestTypeof = !globalObjectType.IsNone // <-- can't pull on types if global types aren't initialized yet
                        && (meaning & SymbolFlags.Type) != 0
                        && !containingQualifiedName.IsNone
                        && Kind(Parent(containingQualifiedName)) != SyntaxKind.TypeOfExpression
                        && !TryGetQualifiedNameAsValue(containingQualifiedName).IsNone;
                    if (canSuggestTypeof)
                    {
                        Error(containingQualifiedName, Diagnostics._0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0,
                            EntityNameToString(containingQualifiedName));
                        return SymbolRef.None;
                    }
                    var nameParent = store.Parent(name.Node);
                    if ((meaning & SymbolFlags.Namespace) != 0 && store.Kind(nameParent) == SyntaxKind.QualifiedName)
                    {
                        var exportedTypeSymbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(ns), rightName, SymbolFlags.Type));
                        if (!exportedTypeSymbol.IsNone)
                        {
                            Error(At(name, store.Right(nameParent)),
                                Diagnostics.Cannot_access_0_1_because_0_is_a_type_but_not_a_namespace_Did_you_mean_to_retrieve_the_type_of_the_property_1_in_0_with_0_1,
                                SymbolToString(exportedTypeSymbol),
                                Utilities.UnescapeLeadingUnderscores(store.Texts.GetString(store.EscapedText(store.Right(nameParent)))!));
                            return SymbolRef.None;
                        }
                    }
                    Error(right, Diagnostics.Namespace_0_has_no_exported_member_1, namespaceName, declarationName);
                }
                return SymbolRef.None;
            }
        }
        else
        {
            throw new InvalidOperationException("Unknown entity name kind.");
        }
        var nameStore = StoreOf(name);
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0 || nameStore.Kind(nameStore.Parent(name.Node)) == SyntaxKind.ExportAssignment)
        {
            MarkSymbolOfAliasDeclarationIfTypeOnly(GetAliasDeclarationFromName(nameStore, name), symbol, SymbolRef.None, overwriteEmpty: true);
        }
        return (SymbolFlagsOf(symbol) & meaning) != 0 || dontResolveAlias ? symbol : ResolveAlias(symbol);
    }

    // utilities.ts getAliasDeclarationFromName
    private static NodeRef GetAliasDeclarationFromName(NodeStore store, NodeRef name)
    {
        var node = name.Node;
        while (true)
        {
            var parent = store.Parent(node);
            switch (store.Kind(parent))
            {
                case SyntaxKind.ImportClause:
                case SyntaxKind.ImportSpecifier:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ExportSpecifier:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.NamespaceExport:
                    return At(name, parent);
                case SyntaxKind.QualifiedName:
                    do
                    {
                        node = store.Parent(node);
                    }
                    while (store.Kind(store.Parent(node)) == SyntaxKind.QualifiedName);
                    continue;
                default:
                    return NodeRef.None;
            }
        }
    }

    private SymbolRef TryGetQualifiedNameAsValue(NodeRef node)
    {
        var store = StoreOf(node);
        var left = Utilities.GetFirstIdentifier(store, node.Node);
        var symbol = ResolveName(At(node, left), store.Texts.GetString(store.EscapedText(left))!, At(node, left), SymbolFlags.Value, null, isUse: true);
        if (symbol.IsNone)
        {
            return SymbolRef.None;
        }
        while (store.Kind(store.Parent(left)) == SyntaxKind.QualifiedName)
        {
            var type = GetTypeOfSymbol(symbol);
            symbol = GetPropertyOfType(type, store.Texts.GetString(store.EscapedText(store.Right(store.Parent(left))))!);
            if (symbol.IsNone)
            {
                return SymbolRef.None;
            }
            left = store.Parent(left);
        }
        return symbol;
    }

    // utilities.ts entityNameToString
    private string EntityNameToString(NodeRef name)
    {
        var store = StoreOf(name);
        return Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, name.Node), name.Node);
    }

    private SymbolRef GetSuggestedSymbolForNonexistentModule(NodeRef name, SymbolRef targetModule)
    {
        if (ExportsOf(targetModule) is null)
        {
            return SymbolRef.None;
        }
        var exports = GetExportsOfModule(targetModule);
        var candidates = new List<SymbolRef>(exports.Count);
        for (var i = 0; i < exports.Count; i++)
        {
            candidates.Add(exports.ValueAt(i));
        }
        var store = StoreOf(name);
        return GetSpellingSuggestionForName(store.Texts.GetString(Utilities.IdText(store, name.Node))!, candidates, SymbolFlags.ModuleMember);
    }

    /// <summary>checker.ts getFullyQualifiedName.</summary>
    private string GetFullyQualifiedName(SymbolRef symbol)
    {
        var parent = ParentOf(symbol);
        return !parent.IsNone ? GetFullyQualifiedName(parent) + "." + SymbolToString(symbol) : SymbolToString(symbol, NodeRef.None, SymbolFlags.None, SymbolFormatFlags.DoNotIncludeSymbolChain | SymbolFormatFlags.AllowAnyNodeKind);
    }

    private static bool IsNonLocalAlias(SymbolFlags flags, SymbolFlags excludes = SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace) =>
        (flags & (SymbolFlags.Alias | excludes)) == SymbolFlags.Alias || ((flags & SymbolFlags.Alias) != 0 && (flags & SymbolFlags.Assignment) != 0);

    /// <summary>checker.ts resolveSymbol.</summary>
    internal SymbolRef ResolveSymbol(SymbolRef symbol, bool dontResolveAlias = false) =>
        !dontResolveAlias && !symbol.IsNone && IsNonLocalAlias(SymbolFlagsOf(symbol)) ? ResolveAlias(symbol) : symbol;

    /// <summary>checker.ts resolveAlias.</summary>
    internal SymbolRef ResolveAlias(SymbolRef symbol)
    {
        ref var links = ref AliasSymbolLinks.Get(symbol);
        if (links.AliasTarget.IsNone)
        {
            links.AliasTarget = resolvingSymbol;
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node.IsNone)
            {
                AliasSymbolLinks.Get(symbol).AliasTarget = unknownSymbol;
                return unknownSymbol;
            }
            var target = GetTargetOfAliasDeclaration(node);
            ref var after = ref AliasSymbolLinks.Get(symbol);
            if (after.AliasTarget == resolvingSymbol)
            {
                after.AliasTarget = target.IsNone ? unknownSymbol : target;
            }
            else
            {
                Error(node, Diagnostics.Circular_definition_of_import_alias_0, SymbolToString(symbol));
            }
        }
        else if (links.AliasTarget == resolvingSymbol)
        {
            links.AliasTarget = unknownSymbol;
        }
        return AliasSymbolLinks.Get(symbol).AliasTarget;
    }

    private SymbolRef TryResolveAlias(SymbolRef symbol) =>
        AliasSymbolLinks.Get(symbol).AliasTarget != resolvingSymbol ? ResolveAlias(symbol) : SymbolRef.None;

    /// <summary>checker.ts getDeclarationOfAliasSymbol (the last alias declaration).</summary>
    internal NodeRef GetDeclarationOfAliasSymbol(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        if (declarations is null)
        {
            return NodeRef.None;
        }
        for (var i = declarations.Count - 1; i >= 0; i--)
        {
            if (IsAliasSymbolDeclaration(declarations[i]))
            {
                return declarations[i];
            }
        }
        return NodeRef.None;
    }

    /// <summary>checker.ts isAliasSymbolDeclaration (the TypeScript forms; JavaScript ones: module C9).</summary>
    internal bool IsAliasSymbolDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.NamespaceExport:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.ShorthandPropertyAssignment:
                return true;
            case SyntaxKind.ImportClause:
                return !store.Name(n).IsNone;
            case SyntaxKind.ExportAssignment:
                return Utilities.ExportAssignmentIsAlias(store, n);
            case SyntaxKind.PropertyAssignment:
                return IsAliasableOrJsExpression(At(node, store.Initializer(n)));
            case SyntaxKind.BinaryExpression:
                return Utilities.GetAssignmentDeclarationKind(store, n) == AssignmentDeclarationKind.ModuleExports && Utilities.ExportAssignmentIsAlias(store, n);
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            {
                var parent = store.Parent(n);
                return store.Kind(parent) == SyntaxKind.BinaryExpression && store.Left(parent) == n && store.Kind(store.OperatorToken(parent)) == SyntaxKind.EqualsToken &&
                    IsAliasableOrJsExpression(At(node, store.Right(parent)));
            }
            case SyntaxKind.VariableDeclaration:
                return !GetExternalModuleRequireArgument(store, n).IsNone;
            case SyntaxKind.BindingElement:
                return !GetExternalModuleRequireArgument(store, store.Parent(store.Parent(n))).IsNone;
            default:
                return false;
        }
    }

    // checker.ts isAliasableOrJsExpression
    private bool IsAliasableOrJsExpression(NodeRef e) =>
        Utilities.IsAliasableExpression(StoreOf(e), e.Node) || (Kind(e) == SyntaxKind.FunctionExpression && IsJSConstructor(e));

    private SymbolRef GetTargetOfAliasDeclaration(NodeRef node, bool dontRecursivelyResolve = false)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.ImportEqualsDeclaration:
                return GetTargetOfImportEqualsDeclaration(node, dontRecursivelyResolve);
            case SyntaxKind.ImportClause:
                return GetTargetOfImportClause(node, dontRecursivelyResolve);
            case SyntaxKind.NamespaceImport:
                return GetTargetOfNamespaceImport(node, dontRecursivelyResolve);
            case SyntaxKind.NamespaceExport:
                return GetTargetOfNamespaceExport(node, dontRecursivelyResolve);
            case SyntaxKind.ImportSpecifier:
            {
                // getTargetOfImportSpecifier
                var resolvedSpecifier = GetExternalModuleMember(At(node, store.Parent(store.Parent(store.Parent(node.Node)))), node, dontRecursivelyResolve);
                MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolvedSpecifier, overwriteEmpty: false);
                return resolvedSpecifier;
            }
            case SyntaxKind.ExportSpecifier:
                return GetTargetOfExportSpecifier(node, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, dontRecursivelyResolve);
            case SyntaxKind.ExportAssignment:
            {
                // getTargetOfExportAssignment
                var resolvedExpression = GetTargetOfAliasLikeExpression(At(node, store.Expression(node.Node)), dontRecursivelyResolve);
                MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolvedExpression, overwriteEmpty: false);
                return resolvedExpression;
            }
            case SyntaxKind.NamespaceExportDeclaration:
            {
                // getTargetOfNamespaceExportDeclaration
                var resolvedModule = ResolveExternalModuleSymbol(NodeSymbol(Parent(node)), dontRecursivelyResolve);
                MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolvedModule, overwriteEmpty: false);
                return resolvedModule;
            }
            case SyntaxKind.ShorthandPropertyAssignment:
                return ResolveEntityName(At(node, store.Name(node.Node)), SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, ignoreErrors: true, dontRecursivelyResolve);
            case SyntaxKind.PropertyAssignment:
                return GetTargetOfAliasLikeExpression(At(node, store.Initializer(node.Node)), dontRecursivelyResolve);
            case SyntaxKind.BinaryExpression:
            {
                // getTargetOfExportAssignment
                var resolved = GetTargetOfAliasLikeExpression(At(node, store.Right(node.Node)), dontRecursivelyResolve);
                MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolved, overwriteEmpty: false);
                return resolved;
            }
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.PropertyAccessExpression:
            {
                // getTargetOfAccessExpression
                var parent = store.Parent(node.Node);
                if (!(store.Kind(parent) == SyntaxKind.BinaryExpression && store.Left(parent) == node.Node && store.Kind(store.OperatorToken(parent)) == SyntaxKind.EqualsToken))
                {
                    return SymbolRef.None;
                }
                return GetTargetOfAliasLikeExpression(At(node, store.Right(parent)), dontRecursivelyResolve);
            }
            case SyntaxKind.VariableDeclaration:
                return GetTargetOfRequireVariableDeclaration(node, dontRecursivelyResolve);
            case SyntaxKind.BindingElement:
            {
                // getTargetOfImportSpecifier (binding elements of `const { x } = require("...")`)
                var root = At(node, Utilities.GetRootDeclaration(store, node.Node));
                var commonJSPropertyAccess = GetCommonJSPropertyAccess(store, root.Node);
                var resolved = GetExternalModuleMember(root, !commonJSPropertyAccess.IsNone ? At(node, commonJSPropertyAccess) : node, dontRecursivelyResolve);
                var name = !store.PropertyName(node.Node).IsNone ? store.PropertyName(node.Node) : store.Name(node.Node);
                if (!commonJSPropertyAccess.IsNone && !resolved.IsNone && store.Kind(name) == SyntaxKind.Identifier)
                {
                    return ResolveSymbol(GetPropertyOfType(GetTypeOfSymbol(resolved), store.Texts.GetString(store.EscapedText(name))!), dontRecursivelyResolve);
                }
                MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolved, overwriteEmpty: false);
                return resolved;
            }
            default:
                return SymbolRef.None;
        }
    }

    // checker.ts getCommonJSPropertyAccess
    private static Node GetCommonJSPropertyAccess(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.VariableDeclaration && !store.Initializer(node).IsNone && store.Kind(store.Initializer(node)) == SyntaxKind.PropertyAccessExpression
            ? store.Initializer(node) : Node.None;

    // utilities.ts getExternalModuleRequireArgument
    private static Node GetExternalModuleRequireArgument(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.VariableDeclaration || store.Initializer(node).IsNone)
        {
            return Node.None;
        }
        var call = GetLeftmostAccessExpression(store, store.Initializer(node));
        if (store.Kind(call) != SyntaxKind.CallExpression)
        {
            return Node.None;
        }
        var expression = store.Expression(call);
        var arguments = store.Arguments(call);
        if (store.Kind(expression) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(expression), "require") || arguments.IsNone || store.Length(arguments) != 1 ||
            !Utilities.IsStringLiteralLike(store, store.Element(arguments, 0)))
        {
            return Node.None;
        }
        return store.Element(arguments, 0);
    }

    // checker.ts getTargetOfImportEqualsDeclaration for a JavaScript `require` variable
    private SymbolRef GetTargetOfRequireVariableDeclaration(NodeRef node, bool dontResolveAlias)
    {
        var store = StoreOf(node);
        var commonJSPropertyAccess = GetCommonJSPropertyAccess(store, node.Node);
        if (!commonJSPropertyAccess.IsNone)
        {
            var call = GetLeftmostAccessExpression(store, store.Expression(commonJSPropertyAccess));
            var name = At(node, store.Element(store.Arguments(call), 0));
            var propertyName = store.Name(commonJSPropertyAccess);
            return store.Kind(propertyName) == SyntaxKind.Identifier
                ? ResolveSymbol(GetPropertyOfType(ResolveExternalModuleTypeByLiteral(name), store.Texts.GetString(store.EscapedText(propertyName))!))
                : SymbolRef.None;
        }
        var argument = GetExternalModuleRequireArgument(store, node.Node);
        if (argument.IsNone)
        {
            return SymbolRef.None;
        }
        var immediate = ResolveExternalModuleName(node, At(node, argument));
        var resolvedModule = ResolveExternalModuleSymbol(immediate);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, immediate, resolvedModule, overwriteEmpty: false);
        return resolvedModule;
    }

    private SymbolRef GetTargetOfAliasLikeExpression(NodeRef expression, bool dontResolveAlias)
    {
        var store = StoreOf(expression);
        var kind = store.Kind(expression.Node);
        if (kind == SyntaxKind.ClassExpression)
        {
            return GetSymbolOfDeclaration(expression); // (checkExpressionCached(expression).symbol)
        }
        if (kind is not (SyntaxKind.Identifier or SyntaxKind.QualifiedName) && !Utilities.IsEntityNameExpression(store, expression.Node))
        {
            return SymbolRef.None;
        }
        var aliasLike = ResolveEntityName(expression, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, ignoreErrors: true, dontResolveAlias);
        if (!aliasLike.IsNone)
        {
            return aliasLike;
        }
        // TODO(C5): checkExpressionCached(expression); getNodeLinks(expression).resolvedSymbol
        return SymbolNodeLinks.Has(expression) ? SymbolNodeLinks.Get(expression).ResolvedSymbol : SymbolRef.None;
    }

    private SymbolRef GetTargetOfImportEqualsDeclaration(NodeRef node, bool dontResolveAlias)
    {
        var store = StoreOf(node);
        var moduleReference = store.ModuleReference(node.Node);
        if (store.Kind(moduleReference) == SyntaxKind.ExternalModuleReference)
        {
            var immediate = ResolveExternalModuleName(node, At(node, store.Expression(moduleReference)));
            var resolvedModule = ResolveExternalModuleSymbol(immediate);
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, immediate, resolvedModule, overwriteEmpty: false);
            return resolvedModule;
        }
        var resolved = GetSymbolOfPartOfRightHandSideOfImportEquals(At(node, moduleReference), dontResolveAlias);
        CheckAndReportErrorForResolvingImportAliasToTypeOnlySymbol(node, resolved);
        return resolved;
    }

    // checker.ts checkAndReportErrorForResolvingImportAliasToTypeOnlySymbol
    private void CheckAndReportErrorForResolvingImportAliasToTypeOnlySymbol(NodeRef node, SymbolRef resolved)
    {
        var store = StoreOf(node);
        if (MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolved, overwriteEmpty: false) && !store.IsTypeOnly(node.Node))
        {
            var typeOnlyDeclaration = GetTypeOnlyAliasDeclaration(GetSymbolOfDeclaration(node));
            if (typeOnlyDeclaration.IsNone)
            {
                return;
            }
            var isExport = Kind(typeOnlyDeclaration) is SyntaxKind.ExportSpecifier or SyntaxKind.ExportDeclaration;
            var message = isExport
                ? Diagnostics.An_import_alias_cannot_reference_a_declaration_that_was_exported_using_export_type
                : Diagnostics.An_import_alias_cannot_reference_a_declaration_that_was_imported_using_import_type;
            var relatedMessage = isExport ? Diagnostics._0_was_exported_here : Diagnostics._0_was_imported_here;
            var typeOnlyStore = StoreOf(typeOnlyDeclaration);
            var name = Kind(typeOnlyDeclaration) == SyntaxKind.ExportDeclaration ? "*" : ModuleExportNameTextUnescaped(typeOnlyStore, typeOnlyStore.Name(typeOnlyDeclaration.Node));
            AddRelatedInfo(Error(At(node, store.ModuleReference(node.Node)), message), CreateRelatedForNode(typeOnlyDeclaration, relatedMessage, name));
        }
    }

    // utilities.ts moduleExportNameTextUnescaped
    private static string ModuleExportNameTextUnescaped(NodeStore store, Node name) =>
        store.Kind(name) == SyntaxKind.StringLiteral ? store.Texts.GetString(store.Text(name))! : Utilities.UnescapeLeadingUnderscores(store.Texts.GetString(store.EscapedText(name))!);

    private SymbolRef GetSymbolOfPartOfRightHandSideOfImportEquals(NodeRef entityName, bool dontResolveAlias)
    {
        // There are three things we might try to look for. In the following examples,
        // the search term is enclosed in |...|:
        //
        //     import a = |b|; // Namespace
        //     import a = |b.c|; // Value, type, namespace
        //     import a = |b.c|.d; // Namespace
        var store = StoreOf(entityName);
        if (store.Kind(entityName.Node) == SyntaxKind.Identifier && Utilities.IsRightSideOfQualifiedNameOrPropertyAccess(store, entityName.Node))
        {
            entityName = Parent(entityName);
        }
        // Check for case 1 and 3 in the above example
        if (store.Kind(entityName.Node) == SyntaxKind.Identifier || store.Kind(store.Parent(entityName.Node)) == SyntaxKind.QualifiedName)
        {
            return ResolveEntityName(entityName, SymbolFlags.Namespace, ignoreErrors: false, dontResolveAlias);
        }
        // Case 2 in above example
        // entityName.kind could be a QualifiedName or a Missing identifier
        return ResolveEntityName(entityName, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, ignoreErrors: false, dontResolveAlias);
    }

    private SymbolRef GetTargetOfImportClause(NodeRef node, bool dontResolveAlias)
    {
        var store = StoreOf(node);
        var moduleSymbol = ResolveExternalModuleName(node, At(node, store.ModuleSpecifier(store.Parent(node.Node))));
        return !moduleSymbol.IsNone ? GetTargetOfModuleDefault(moduleSymbol, node, dontResolveAlias) : SymbolRef.None;
    }

    // checker.ts getTargetofModuleDefault, for ambient modules (Gate C resolves no files): a default export, else the
    // synthetic default of allowSyntheticDefaultImports (the module, or its export=).
    private SymbolRef GetTargetOfModuleDefault(SymbolRef moduleSymbol, NodeRef node, bool dontResolveAlias)
    {
        if (IsShorthandAmbientModuleSymbol(moduleSymbol))
        {
            return moduleSymbol;
        }
        var exports = ExportsOf(moduleSymbol);
        SymbolRef exportDefaultSymbol;
        if (exports is not null && exports.Has(InternalSymbolName.ExportEquals))
        {
            exportDefaultSymbol = ResolveSymbol(exports.Get(InternalSymbolName.ExportEquals), dontResolveAlias);
        }
        else
        {
            exportDefaultSymbol = ResolveSymbol(exports?.Get(InternalSymbolName.Default) ?? SymbolRef.None, dontResolveAlias);
        }
        if (exportDefaultSymbol.IsNone && compilerOptions.GetAllowSyntheticDefaultImports())
        {
            // per emit behavior, a synthetic default overrides a "real" .default member if `__esModule` is not present
            var resolved = ResolveExternalModuleSymbol(moduleSymbol, dontResolveAlias);
            if (resolved.IsNone)
            {
                resolved = ResolveSymbol(moduleSymbol, dontResolveAlias);
            }
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, moduleSymbol, resolved, overwriteEmpty: false);
            return resolved;
        }
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, exportDefaultSymbol, SymbolRef.None, overwriteEmpty: false);
        return exportDefaultSymbol;
    }

    // checker.ts isShorthandAmbientModuleSymbol
    private bool IsShorthandAmbientModuleSymbol(SymbolRef moduleSymbol)
    {
        var valueDeclaration = ValueDeclarationOf(moduleSymbol);
        if (valueDeclaration.IsNone)
        {
            return false;
        }
        var store = StoreOf(valueDeclaration);
        return store.Kind(valueDeclaration.Node) == SyntaxKind.ModuleDeclaration && store.Body(valueDeclaration.Node).IsNone;
    }

    private SymbolRef GetTargetOfNamespaceImport(NodeRef node, bool dontResolveAlias)
    {
        var store = StoreOf(node);
        var moduleSpecifier = store.ModuleSpecifier(store.Parent(store.Parent(node.Node)));
        var immediate = ResolveExternalModuleName(node, At(node, moduleSpecifier));
        var resolved = ResolveExternalModuleSymbol(immediate, dontResolveAlias);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, immediate, resolved, overwriteEmpty: false);
        return resolved;
    }

    private SymbolRef GetTargetOfNamespaceExport(NodeRef node, bool dontResolveAlias)
    {
        var store = StoreOf(node);
        var moduleSpecifier = store.ModuleSpecifier(store.Parent(node.Node));
        var immediate = ResolveExternalModuleName(node, At(node, moduleSpecifier));
        var resolved = ResolveExternalModuleSymbol(immediate, dontResolveAlias);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, immediate, resolved, overwriteEmpty: false);
        return resolved;
    }

    private SymbolRef GetTargetOfExportSpecifier(NodeRef node, SymbolFlags meaning, bool dontResolveAlias)
    {
        var store = StoreOf(node);
        var name = !store.PropertyName(node.Node).IsNone ? store.PropertyName(node.Node) : store.Name(node.Node);
        var declaration = store.Parent(store.Parent(node.Node));
        if (Utilities.ModuleExportNameIsDefault(store, name))
        {
            var specifier = store.ModuleSpecifier(declaration);
            var moduleSymbol = specifier.IsNone ? SymbolRef.None : ResolveExternalModuleName(node, At(node, specifier));
            if (!moduleSymbol.IsNone)
            {
                return GetTargetOfModuleDefault(moduleSymbol, node, dontResolveAlias);
            }
        }
        var resolvedSpecifier = !store.ModuleSpecifier(declaration).IsNone
            ? GetExternalModuleMember(At(node, declaration), node, dontResolveAlias)
            : store.Kind(name) == SyntaxKind.StringLiteral ? SymbolRef.None // Skip for invalid syntax like this: export { "x" }
            : ResolveEntityName(At(node, name), meaning, ignoreErrors: false, dontResolveAlias);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, SymbolRef.None, resolvedSpecifier, overwriteEmpty: false);
        return resolvedSpecifier;
    }

    // checker.ts getExternalModuleMember, for ambient modules.
    private SymbolRef GetExternalModuleMember(NodeRef declaration, NodeRef specifier, bool dontResolveAlias)
    {
        var store = StoreOf(specifier);
        var requireArgument = GetExternalModuleRequireArgument(store, declaration.Node);
        var moduleSpecifier = At(declaration, !requireArgument.IsNone ? requireArgument : store.ModuleSpecifier(declaration.Node));
        var moduleSymbol = ResolveExternalModuleName(specifier, moduleSpecifier);
        var name = store.Kind(specifier.Node) != SyntaxKind.PropertyAccessExpression && !store.PropertyName(specifier.Node).IsNone ? store.PropertyName(specifier.Node) : store.Name(specifier.Node);
        if (store.Kind(name) is not (SyntaxKind.Identifier or SyntaxKind.StringLiteral))
        {
            return SymbolRef.None;
        }
        var nameText = store.Texts.GetString(store.Kind(name) == SyntaxKind.Identifier ? store.EscapedText(name) : store.Text(name))!;
        if (store.Kind(name) == SyntaxKind.StringLiteral)
        {
            nameText = Utilities.EscapeLeadingUnderscores(nameText);
        }
        var targetSymbol = ResolveExternalModuleSymbol(moduleSymbol);
        if (targetSymbol.IsNone)
        {
            return SymbolRef.None;
        }
        if (IsShorthandAmbientModuleSymbol(moduleSymbol))
        {
            return moduleSymbol;
        }
        // (symbolFromVariable: properties of an export= variable, TODO(C5))
        var symbolFromModule = GetExportOfModule(targetSymbol, nameText, specifier, dontResolveAlias);
        if (symbolFromModule.IsNone && nameText == InternalSymbolName.Default && compilerOptions.GetAllowSyntheticDefaultImports())
        {
            var resolved = ResolveExternalModuleSymbol(moduleSymbol, dontResolveAlias);
            symbolFromModule = !resolved.IsNone ? resolved : ResolveSymbol(moduleSymbol, dontResolveAlias);
        }
        if (symbolFromModule.IsNone)
        {
            ErrorNoModuleMemberSymbol(moduleSymbol, targetSymbol, At(specifier, name), nameText);
        }
        return symbolFromModule;
    }

    private SymbolRef GetExportOfModule(SymbolRef symbol, string name, NodeRef specifier, bool dontResolveAlias)
    {
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Module) != 0)
        {
            var exportSymbol = GetExportsOfSymbol(symbol).Get(name);
            var resolved = ResolveSymbol(exportSymbol, dontResolveAlias);
            // (typeOnlyExportStarMap: `export type *` re-exports need modules Gate C does not resolve)
            MarkSymbolOfAliasDeclarationIfTypeOnly(specifier, exportSymbol, resolved, overwriteEmpty: false);
            return resolved;
        }
        return SymbolRef.None;
    }

    // checker.ts reportNonExportedMember
    private void ReportNonExportedMember(NodeRef name, string nameText, string declarationName, SymbolRef moduleSymbol, string moduleName)
    {
        var valueDeclaration = ValueDeclarationOf(moduleSymbol);
        var localSymbol = valueDeclaration.IsNone ? SymbolRef.None : LocalsOf(valueDeclaration)?.Get(nameText) ?? SymbolRef.None;
        var exports = ExportsOf(moduleSymbol);
        if (localSymbol.IsNone)
        {
            Error(name, Diagnostics.Module_0_has_no_exported_member_1, moduleName, declarationName);
            return;
        }
        var exportedEqualsSymbol = exports?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
        if (!exportedEqualsSymbol.IsNone)
        {
            if (IsSameReference(exportedEqualsSymbol, localSymbol))
            {
                // reportInvalidImportEqualsExportMember (module kinds ES2015 and later)
                Error(name, compilerOptions.GetESModuleInterop()
                    ? Diagnostics._0_can_only_be_imported_by_using_a_default_import
                    : Diagnostics._0_can_only_be_imported_by_turning_on_the_esModuleInterop_flag_and_using_a_default_import, declarationName);
            }
            else
            {
                Error(name, Diagnostics.Module_0_has_no_exported_member_1, moduleName, declarationName);
            }
            return;
        }
        var exportedSymbol = SymbolRef.None;
        if (exports is not null)
        {
            for (var i = 0; i < exports.Count; i++)
            {
                if (IsSameReference(exports.ValueAt(i), localSymbol))
                {
                    exportedSymbol = exports.ValueAt(i);
                    break;
                }
            }
        }
        var diagnostic = !exportedSymbol.IsNone
            ? Error(name, Diagnostics.Module_0_declares_1_locally_but_it_is_exported_as_2, moduleName, declarationName, SymbolToString(exportedSymbol))
            : Error(name, Diagnostics.Module_0_declares_1_locally_but_it_is_not_exported, moduleName, declarationName);
        if (DeclarationsOf(localSymbol) is { } declarations)
        {
            var related = new ProgramRelatedInformation[declarations.Count];
            for (var i = 0; i < declarations.Count; i++)
            {
                related[i] = CreateRelatedForNode(declarations[i], i == 0 ? Diagnostics._0_is_declared_here : Diagnostics.and_here, declarationName);
            }
            AddRelatedInfo(diagnostic, related);
        }
    }

    // checker.ts getSymbolIfSameReference (as a test)
    private bool IsSameReference(SymbolRef s1, SymbolRef s2) =>
        GetMergedSymbol(ResolveSymbol(GetMergedSymbol(s1), dontResolveAlias: false)) == GetMergedSymbol(ResolveSymbol(GetMergedSymbol(s2), dontResolveAlias: false));

    private void ErrorNoModuleMemberSymbol(SymbolRef moduleSymbol, SymbolRef targetSymbol, NodeRef name, string nameText)
    {
        var moduleName = GetFullyQualifiedName(moduleSymbol);
        var store = StoreOf(name);
        var declarationName = Utilities.DeclarationNameToString(store, name.Node);
        var exports = ExportsOf(targetSymbol);
        var suggestion = store.Kind(name.Node) == SyntaxKind.Identifier ? GetSuggestedSymbolForNonexistentModule(name, targetSymbol) : SymbolRef.None;
        if (!suggestion.IsNone)
        {
            var suggestionName = SymbolToString(suggestion);
            var d = Error(name, Diagnostics._0_has_no_exported_member_named_1_Did_you_mean_2, moduleName, declarationName, suggestionName);
            var suggestionValueDeclaration = ValueDeclarationOf(suggestion);
            if (!suggestionValueDeclaration.IsNone)
            {
                d.RelatedInformation = [CreateRelatedForNode(suggestionValueDeclaration, Diagnostics._0_is_declared_here, suggestionName)];
            }
        }
        else if (exports is not null && exports.Has(InternalSymbolName.Default))
        {
            Error(name, Diagnostics.Module_0_has_no_exported_member_1_Did_you_mean_to_use_import_1_from_0_instead, moduleName, declarationName);
        }
        else
        {
            ReportNonExportedMember(name, nameText, declarationName, moduleSymbol, moduleName);
        }
        _ = nameText;
    }

    /// <summary>checker.ts getSymbolFlags: the combined flags of a symbol and all alias targets it resolves to.</summary>
    internal SymbolFlags GetSymbolFlags(SymbolRef symbol, bool excludeTypeOnlyMeanings = false, bool excludeLocalMeanings = false)
    {
        // (type-only export stars: `export type *` re-exports need modules Gate C does not resolve)
        var typeOnlyDeclaration = excludeTypeOnlyMeanings ? GetTypeOnlyAliasDeclaration(symbol) : NodeRef.None;
        var typeOnlyResolution = typeOnlyDeclaration.IsNone ? SymbolRef.None : ResolveAlias(GetSymbolOfDeclaration(typeOnlyDeclaration));
        var flags = excludeLocalMeanings ? SymbolFlags.None : SymbolFlagsOf(symbol);
        HashSet<SymbolRef>? seenSymbols = null;
        while ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0)
        {
            var target = GetExportSymbolOfValueSymbolIfExported(ResolveAlias(symbol));
            if (!typeOnlyResolution.IsNone && target == typeOnlyResolution)
            {
                break;
            }
            if (target == unknownSymbol)
            {
                return SymbolFlags.All;
            }
            // Optimizations - try to avoid creating or adding to
            // `seenSymbols` if possible
            if (target == symbol || (seenSymbols is not null && seenSymbols.Contains(target)))
            {
                break;
            }
            if ((SymbolFlagsOf(target) & SymbolFlags.Alias) != 0)
            {
                if (seenSymbols is not null)
                {
                    seenSymbols.Add(target);
                }
                else
                {
                    seenSymbols = [symbol, target];
                }
            }
            flags |= SymbolFlagsOf(target);
            symbol = target;
        }
        return flags;
    }

    // ---- external modules ----

    private static readonly HashSet<string> NodeCoreModules = CreateNodeCoreModules();

    private static HashSet<string> CreateNodeCoreModules()
    {
        string[] unprefixed =
        [
            "assert", "assert/strict", "async_hooks", "buffer", "child_process", "cluster", "console", "constants", "crypto", "dgram",
            "diagnostics_channel", "dns", "dns/promises", "domain", "events", "fs", "fs/promises", "http", "http2", "https", "inspector",
            "inspector/promises", "module", "net", "os", "path", "path/posix", "path/win32", "perf_hooks", "process", "punycode",
            "querystring", "readline", "readline/promises", "repl", "stream", "stream/consumers", "stream/promises", "stream/web",
            "string_decoder", "sys", "timers", "timers/promises", "tls", "trace_events", "tty", "url", "util", "util/types", "v8", "vm",
            "wasi", "worker_threads", "zlib",
        ];
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in unprefixed)
        {
            set.Add(name);
            set.Add("node:" + name);
        }
        foreach (var name in (string[])["node:quic", "node:sea", "node:sqlite", "node:test", "node:test/reporters"])
        {
            set.Add(name);
        }
        return set;
    }

    private DiagnosticMessage? GetCannotResolveModuleNameErrorForSpecificModule(NodeRef moduleName)
    {
        var store = StoreOf(moduleName);
        if (store.Kind(moduleName.Node) == SyntaxKind.StringLiteral && NodeCoreModules.Contains(store.Texts.GetString(store.Text(moduleName.Node))!))
        {
            return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig;
        }
        return null;
    }

    /// <summary>checker.ts resolveExternalModuleName.</summary>
    internal SymbolRef ResolveExternalModuleName(NodeRef location, NodeRef moduleReferenceExpression, bool ignoreErrors = false, DiagnosticMessage? errorMessage = null)
    {
        if (moduleReferenceExpression.IsNone)
        {
            return SymbolRef.None;
        }
        var isClassic = compilerOptions.GetEmitModuleResolutionKind() == ModuleResolutionKind.Classic;
        errorMessage ??= GetCannotResolveModuleNameErrorForSpecificModule(moduleReferenceExpression) ?? (isClassic
            ? Diagnostics.Cannot_find_module_0_Did_you_mean_to_set_the_moduleResolution_option_to_nodenext_or_to_add_aliases_to_the_paths_option
            : Diagnostics.Cannot_find_module_0_or_its_corresponding_type_declarations);
        return ResolveExternalModuleNameWorker(location, moduleReferenceExpression, ignoreErrors ? null : errorMessage, ignoreErrors);
    }

    private SymbolRef ResolveExternalModuleNameWorker(NodeRef location, NodeRef moduleReferenceExpression, DiagnosticMessage? moduleNotFoundError, bool ignoreErrors = false, bool isForAugmentation = false)
    {
        if (moduleReferenceExpression.IsNone)
        {
            return SymbolRef.None;
        }
        var store = StoreOf(moduleReferenceExpression);
        if (!Utilities.IsStringLiteralLike(store, moduleReferenceExpression.Node))
        {
            return SymbolRef.None;
        }
        return ResolveExternalModule(location, store.Texts.GetString(store.Text(moduleReferenceExpression.Node))!, moduleNotFoundError,
            !ignoreErrors ? moduleReferenceExpression : NodeRef.None, isForAugmentation);
    }

    /// <summary>
    /// checker.ts resolveExternalModule. Track O's programs use noResolve (Gate C), so host.getResolvedModule finds no
    /// module: only ambient modules (and, TODO(C9e), pattern ambient modules) resolve; otherwise the not-found error.
    /// </summary>
    private SymbolRef ResolveExternalModule(NodeRef location, string moduleReference, DiagnosticMessage? moduleNotFoundError, NodeRef errorNode, bool isForAugmentation)
    {
        if (!errorNode.IsNone && moduleReference.StartsWith("@types/", StringComparison.Ordinal))
        {
            var withoutAtTypePrefix = moduleReference["@types/".Length..];
            Error(errorNode, Diagnostics.Cannot_import_type_declaration_files_Consider_importing_0_instead_of_1, withoutAtTypePrefix, moduleReference);
        }
        var ambientModule = TryFindAmbientModule(moduleReference, withAugmentations: true);
        if (!ambientModule.IsNone)
        {
            return ambientModule;
        }
        if (errorNode.IsNone)
        {
            return SymbolRef.None;
        }
        if (moduleNotFoundError is not null)
        {
            // (resolutionDiagnostic, resolveJsonModule, node16 extension suggestions: need a resolved module or a module
            // resolution mode Gate C does not use)
            Error(errorNode, moduleNotFoundError, moduleReference);
        }
        _ = location;
        _ = isForAugmentation;
        return SymbolRef.None;
    }
}
