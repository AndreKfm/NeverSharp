// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (getSymbolAtLocation, getSymbolOfNameOrPropertyAccessExpression,
// getTypeOfNode, getRegularTypeOfExpression, getImmediateAliasedSymbol and their helpers)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/services.go (GetSymbolAtLocation, GetTypeAtLocation)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1; module C9b: JSX intrinsic tags. TODO(C9c): JSDoc names and
// JavaScript special property assignments; TODO(C9d): meta properties; TODO(C5): destructuring patterns, element
// access, index symbols.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>checker.ts getTypeAtLocation(node): getTypeOfNode of a parse tree node.</summary>
    internal Type GetTypeAtLocation(NodeRef node)
    {
        CheckNode(node);
        return GetTypeOfNode(node);
    }

    /// <summary>checker.ts getSymbolAtLocation(node) (the public form: ignoreErrors).</summary>
    internal SymbolRef GetSymbolAtLocationPublic(NodeRef node)
    {
        CheckNode(node);
        return GetSymbolAtLocation(node, ignoreErrors: true);
    }

    /// <summary>checker.ts getSymbolAtLocation.</summary>
    internal SymbolRef GetSymbolAtLocation(NodeRef node, bool ignoreErrors = false)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        if (kind == SyntaxKind.SourceFile)
        {
            return Utilities.IsExternalModule(store, n) ? GetMergedSymbol(NodeSymbol(node)) : SymbolRef.None;
        }
        var parent = store.Parent(n);
        var grandParent = store.Parent(parent);
        if ((store.Flags(n) & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return SymbolRef.None;
        }
        if (IsDeclarationNameOrImportPropertyName(store, n))
        {
            // This is a declaration, call getSymbolOfNode
            var parentSymbol = GetSymbolOfDeclaration(At(node, parent));
            return store.Kind(parent) is SyntaxKind.ImportSpecifier or SyntaxKind.ExportSpecifier && store.PropertyName(parent) == n
                ? GetImmediateAliasedSymbol(parentSymbol)
                : parentSymbol;
        }
        if (IsLiteralComputedPropertyDeclarationName(store, n))
        {
            return GetSymbolOfDeclaration(At(node, grandParent));
        }
        if (kind == SyntaxKind.Identifier)
        {
            if (IsInRightSideOfImportOrExportAssignment(store, n))
            {
                return GetSymbolOfNameOrPropertyAccessExpression(node);
            }
            if (store.Kind(parent) == SyntaxKind.BindingElement && store.Kind(grandParent) == SyntaxKind.ObjectBindingPattern && n == store.PropertyName(parent))
            {
                var typeOfPattern = GetTypeOfNode(At(node, grandParent));
                var propertyDeclaration = GetPropertyOfType(typeOfPattern, store.Texts.GetString(store.EscapedText(n))!);
                if (!propertyDeclaration.IsNone)
                {
                    return propertyDeclaration;
                }
            }
            else if (store.Kind(parent) == SyntaxKind.MetaProperty && store.Name(parent) == n)
            {
                // the `meta` of import.meta: the transient member of getGlobalImportMetaExpressionType (TODO(C5): new.target)
                if (store.KeywordToken(parent) == SyntaxKind.ImportKeyword && store.Texts.Equals(store.EscapedText(n), "meta"))
                {
                    return GetImportMetaExpressionMetaSymbol();
                }
                return SymbolRef.None;
            }
        }
        switch (kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.QualifiedName:
                if (!IsThisInTypeQuery(store, n))
                {
                    return GetSymbolOfNameOrPropertyAccessExpression(node);
                }
                goto case SyntaxKind.ThisKeyword;
            case SyntaxKind.ThisKeyword:
            {
                var container = Utilities.GetThisContainer(store, n, includeArrowFunctions: false, includeClassComputedPropertyName: false);
                if (!container.IsNone && Utilities.IsFunctionLike(store, container))
                {
                    var sig = GetSignatureFromDeclaration(At(node, container));
                    var thisParameter = Signatures.Record(sig).ThisParameter;
                    if (!thisParameter.IsNone)
                    {
                        return thisParameter;
                    }
                }
                if (Utilities.IsInExpressionContext(store, n))
                {
                    return Types.Symbol(CheckExpression(node));
                }
                goto case SyntaxKind.ThisType;
            }
            case SyntaxKind.ThisType:
                return Types.Symbol(GetTypeFromThisTypeNode(node));
            case SyntaxKind.SuperKeyword:
                return Types.Symbol(CheckExpression(node));
            case SyntaxKind.ConstructorKeyword:
                // constructor keyword for an overload, should take us to the definition if it exist
                if (!parent.IsNone && store.Kind(parent) == SyntaxKind.Constructor)
                {
                    return NodeSymbol(At(node, store.Parent(parent)));
                }
                return SymbolRef.None;
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                // 1). import x = require("./mo/*gotToDefinitionHere*/d")
                // 2). External module name in an import declaration
                // 3). Dynamic import call or require in javascript
                // 4). type A = import("./f/*gotToDefinitionHere*/oo")
                if ((Utilities.IsExternalModuleImportEqualsDeclaration(store, grandParent) && store.Expression(store.ModuleReference(grandParent)) == n) ||
                    (store.Kind(parent) is SyntaxKind.ImportDeclaration or SyntaxKind.ExportDeclaration && store.ModuleSpecifier(parent) == n) ||
                    Utilities.IsImportCall(store, parent) ||
                    (store.Kind(parent) == SyntaxKind.LiteralType && store.Kind(grandParent) == SyntaxKind.ImportType && store.Argument(grandParent) == parent))
                {
                    return ResolveExternalModuleName(node, node, ignoreErrors);
                }
                goto case SyntaxKind.NumericLiteral;
            case SyntaxKind.NumericLiteral:
            {
                // index access
                var objectType = Type.None;
                if (store.Kind(parent) == SyntaxKind.ElementAccessExpression)
                {
                    if (store.ArgumentExpression(parent) == n)
                    {
                        objectType = CheckExpressionCached(At(node, store.Expression(parent)));
                    }
                }
                else if (store.Kind(parent) == SyntaxKind.LiteralType && store.Kind(grandParent) == SyntaxKind.IndexedAccessType)
                {
                    objectType = GetTypeFromTypeNode(At(node, store.ObjectType(grandParent)));
                }
                return objectType.IsNone ? SymbolRef.None : GetPropertyOfType(objectType, Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(n))!));
            }
            case SyntaxKind.DefaultKeyword:
            case SyntaxKind.FunctionKeyword:
            case SyntaxKind.EqualsGreaterThanToken:
            case SyntaxKind.ClassKeyword:
                return GetSymbolOfNode(At(node, parent));
            case SyntaxKind.ImportType:
            {
                var argument = store.Argument(n);
                return store.Kind(argument) == SyntaxKind.LiteralType && Utilities.IsStringLiteralLike(store, store.Literal(argument))
                    ? GetSymbolAtLocation(At(node, store.Literal(argument)), ignoreErrors)
                    : SymbolRef.None;
            }
            case SyntaxKind.ExportKeyword:
                return store.Kind(parent) == SyntaxKind.ExportAssignment ? NodeSymbol(At(node, parent)) : SymbolRef.None;
            case SyntaxKind.MetaProperty:
            {
                var metaType = CheckExpressionCached(node);
                return IsErrorType(metaType) || (Types.Flags(metaType) & TypeFlags.Object) == 0 ? SymbolRef.None : Types.Symbol(metaType);
            }
            case SyntaxKind.JsxNamespacedName:
                if (Utilities.IsJsxTagName(store, n) && IsJsxIntrinsicTagName(store, n))
                {
                    var symbol = GetIntrinsicTagSymbol(At(node, parent));
                    return symbol == unknownSymbol ? SymbolRef.None : symbol;
                }
                return SymbolRef.None;
            default:
                // (instanceof: C5)
                return SymbolRef.None;
        }
    }

    private SymbolRef importMetaExpressionMetaSymbol;

    // checker.ts getGlobalImportMetaExpressionType: `ImportMetaExpression { meta: ImportMeta }` (its `meta` member)
    private SymbolRef GetImportMetaExpressionMetaSymbol()
    {
        if (importMetaExpressionMetaSymbol.IsNone)
        {
            var symbol = CreateSymbol(SymbolFlags.None, "ImportMetaExpression");
            var meta = CreateSymbol(SymbolFlags.Property, "meta", CheckFlags.Readonly);
            TransientSymbols.Record(meta).Parent = symbol;
            var importMeta = GetGlobalType("ImportMeta", 0, reportErrors: true);
            ValueSymbolLinks.Get(meta).ResolvedType = importMeta.IsNone ? errorType : importMeta;
            var members = new SymTable();
            members.Set("meta", meta);
            TransientSymbols.Record(symbol).Members = members;
            importMetaExpressionMetaSymbol = meta;
        }
        return importMetaExpressionMetaSymbol;
    }

    private static bool IsDeclarationNameOrImportPropertyName(NodeStore store, Node name)
    {
        var parent = store.Parent(name);
        switch (store.Kind(parent))
        {
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
                return store.Kind(name) is SyntaxKind.Identifier or SyntaxKind.StringLiteral;
            default:
                return Utilities.IsDeclarationName(store, name);
        }
    }

    // utilities.ts isLiteralComputedPropertyDeclarationName
    private static bool IsLiteralComputedPropertyDeclarationName(NodeStore store, Node node)
    {
        if (store.Kind(node) is not (SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.NoSubstitutionTemplateLiteral))
        {
            return false;
        }
        var parent = store.Parent(node);
        return store.Kind(parent) == SyntaxKind.ComputedPropertyName && Utilities.IsDeclaration(store, store.Parent(parent));
    }

    private static bool IsThisInTypeQuery(NodeStore store, Node node)
    {
        if (!Utilities.IsThisIdentifier(store, node))
        {
            return false;
        }
        while (store.Kind(store.Parent(node)) == SyntaxKind.QualifiedName && store.Left(store.Parent(node)) == node)
        {
            node = store.Parent(node);
        }
        return store.Kind(store.Parent(node)) == SyntaxKind.TypeQuery;
    }

    // checker.ts getLeftSideOfImportEqualsOrExportAssignment !== undefined
    private static bool IsInRightSideOfImportOrExportAssignment(NodeStore store, Node nodeOnRightSide)
    {
        while (store.Kind(store.Parent(nodeOnRightSide)) == SyntaxKind.QualifiedName)
        {
            nodeOnRightSide = store.Parent(nodeOnRightSide);
        }
        var parent = store.Parent(nodeOnRightSide);
        return store.Kind(parent) switch
        {
            SyntaxKind.ImportEqualsDeclaration => store.ModuleReference(parent) == nodeOnRightSide,
            SyntaxKind.ExportAssignment => store.Expression(parent) == nodeOnRightSide,
            _ => false,
        };
    }

    private static bool IsInNameOfExpressionWithTypeArguments(NodeStore store, Node node)
    {
        while (store.Kind(store.Parent(node)) == SyntaxKind.PropertyAccessExpression)
        {
            node = store.Parent(node);
        }
        return store.Kind(store.Parent(node)) == SyntaxKind.ExpressionWithTypeArguments;
    }

    /// <summary>checker.ts getImmediateAliasedSymbol.</summary>
    internal SymbolRef GetImmediateAliasedSymbol(SymbolRef symbol)
    {
        ref var links = ref AliasSymbolLinks.Get(symbol);
        if (links.ImmediateTarget.IsNone)
        {
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node.IsNone)
            {
                return SymbolRef.None;
            }
            var target = GetTargetOfAliasDeclaration(node, dontRecursivelyResolve: true);
            AliasSymbolLinks.Get(symbol).ImmediateTarget = target.IsNone ? resolvingSymbol : target;
        }
        var result = AliasSymbolLinks.Get(symbol).ImmediateTarget;
        return result == resolvingSymbol ? SymbolRef.None : result;
    }

    /// <summary>checker.ts getSymbolOfNameOrPropertyAccessExpression.</summary>
    private SymbolRef GetSymbolOfNameOrPropertyAccessExpression(NodeRef nameRef)
    {
        var store = StoreOf(nameRef);
        var name = nameRef.Node;
        if (Utilities.IsDeclarationName(store, name))
        {
            return GetSymbolOfNode(At(nameRef, store.Parent(name)));
        }
        // (JavaScript special property assignments: module C9)
        var parent = store.Parent(name);
        var nameKind = store.Kind(name);
        var isEntityName = nameKind is SyntaxKind.Identifier or SyntaxKind.QualifiedName;
        if (store.Kind(parent) == SyntaxKind.ExportAssignment && Utilities.IsEntityNameExpression(store, name))
        {
            // Even an entity name expression that doesn't resolve as an entityname may still typecheck as a property access expression
            var success = ResolveEntityName(nameRef, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, ignoreErrors: true);
            if (!success.IsNone && success != unknownSymbol)
            {
                return success;
            }
        }
        else if (isEntityName && IsInRightSideOfImportOrExportAssignment(store, name))
        {
            // Since we already checked for ExportAssignment, this really could only be an Import
            return GetSymbolOfPartOfRightHandSideOfImportEquals(nameRef, dontResolveAlias: true);
        }
        if (isEntityName)
        {
            // isImportTypeQualifierPart (module C3)
            var qualifierNode = name;
            var qualifierParent = parent;
            while (store.Kind(qualifierParent) == SyntaxKind.QualifiedName)
            {
                qualifierNode = qualifierParent;
                qualifierParent = store.Parent(qualifierParent);
            }
            if (!qualifierParent.IsNone && store.Kind(qualifierParent) == SyntaxKind.ImportType && store.Qualifier(qualifierParent) == qualifierNode)
            {
                GetTypeFromTypeNode(At(nameRef, qualifierParent));
                var sym = SymbolNodeLinks.Has(nameRef) ? SymbolNodeLinks.Get(nameRef).ResolvedSymbol : SymbolRef.None;
                return sym == unknownSymbol ? SymbolRef.None : sym;
            }
        }
        while (Utilities.IsRightSideOfQualifiedNameOrPropertyAccess(store, name) || (store.Kind(store.Parent(name)) == SyntaxKind.JSDocMemberName && store.Right(store.Parent(name)) == name))
        {
            name = store.Parent(name);
        }
        nameRef = At(nameRef, name);
        parent = store.Parent(name);
        nameKind = store.Kind(name);
        isEntityName = nameKind is SyntaxKind.Identifier or SyntaxKind.QualifiedName;
        if (IsInNameOfExpressionWithTypeArguments(store, name))
        {
            SymbolFlags meaning;
            if (store.Kind(parent) == SyntaxKind.ExpressionWithTypeArguments)
            {
                // An 'ExpressionWithTypeArguments' may appear in type space (interface Foo extends Bar<T>),
                // value space (return foo<T>), or both(class Foo extends Bar<T>); ensure the meaning matches.
                meaning = Utilities.IsPartOfTypeNode(store, name) ? SymbolFlags.Type : SymbolFlags.Value;
                // In a class 'extends' clause we are also looking for a value.
                if (Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(store, parent))
                {
                    meaning |= SymbolFlags.Value;
                }
            }
            else
            {
                meaning = SymbolFlags.Namespace;
            }
            meaning |= SymbolFlags.Alias;
            var entityNameSymbol = Utilities.IsEntityNameExpression(store, name) ? ResolveEntityName(nameRef, meaning, ignoreErrors: true) : SymbolRef.None;
            if (!entityNameSymbol.IsNone)
            {
                return entityNameSymbol;
            }
        }
        // (JSDoc parameter tags and template tags: module C9)
        if (Utilities.IsExpressionNode(store, name))
        {
            if (Utilities.NodeIsMissing(store, name))
            {
                // Missing entity name.
                return SymbolRef.None;
            }
            var isJSDoc = IsInJSDocReference(store, name);
            var meaning = isJSDoc ? SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Value : SymbolFlags.Value;
            if (nameKind == SyntaxKind.Identifier)
            {
                if (Utilities.IsJsxTagName(store, name) && IsJsxIntrinsicTagName(store, name))
                {
                    var symbol = GetIntrinsicTagSymbol(At(nameRef, store.Parent(name)));
                    return symbol == unknownSymbol ? SymbolRef.None : symbol;
                }
                return ResolveEntityName(nameRef, meaning, ignoreErrors: true, dontResolveAlias: true);
            }
            if (nameKind == SyntaxKind.PrivateIdentifier)
            {
                // TODO(C5): getSymbolForPrivateIdentifierExpression
                return SymbolRef.None;
            }
            if (nameKind is SyntaxKind.PropertyAccessExpression or SyntaxKind.QualifiedName)
            {
                if (SymbolNodeLinks.Has(nameRef) && !SymbolNodeLinks.Get(nameRef).ResolvedSymbol.IsNone)
                {
                    return SymbolNodeLinks.Get(nameRef).ResolvedSymbol;
                }
                if (nameKind == SyntaxKind.PropertyAccessExpression)
                {
                    CheckPropertyAccessExpression(nameRef);
                }
                else
                {
                    CheckQualifiedName(nameRef);
                }
                return SymbolNodeLinks.Has(nameRef) ? SymbolNodeLinks.Get(nameRef).ResolvedSymbol : SymbolRef.None;
            }
            return SymbolRef.None;
        }
        if (isEntityName && Utilities.IsTypeReferenceIdentifier(store, name))
        {
            var meaning = store.Kind(parent) == SyntaxKind.TypeReference ? SymbolFlags.Type : SymbolFlags.Namespace;
            var symbol = ResolveEntityName(nameRef, meaning, ignoreErrors: true, dontResolveAlias: true);
            return !symbol.IsNone && symbol != unknownSymbol ? symbol : GetUnresolvedSymbolForEntityName(nameRef);
        }
        if (store.Kind(parent) == SyntaxKind.TypePredicate)
        {
            return ResolveEntityName(nameRef, SymbolFlags.FunctionScopedVariable, ignoreErrors: true);
        }
        return SymbolRef.None;
    }

    private static bool IsInJSDocReference(NodeStore store, Node node)
    {
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) is SyntaxKind.JSDocLink or SyntaxKind.JSDocLinkCode or SyntaxKind.JSDocLinkPlain or SyntaxKind.JSDocNameReference or SyntaxKind.JSDocMemberName)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts isJsxIntrinsicTagName
    private static bool IsJsxIntrinsicTagName(NodeStore store, Node tagName) =>
        (store.Kind(tagName) == SyntaxKind.Identifier && Utilities.IsIntrinsicJsxName(store.Texts.GetString(store.EscapedText(tagName))!))
        || store.Kind(tagName) == SyntaxKind.JsxNamespacedName;

    /// <summary>checker.ts getTypeOfNode.</summary>
    internal Type GetTypeOfNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        if (kind == SyntaxKind.SourceFile && !Utilities.IsExternalModule(store, n))
        {
            return errorType;
        }
        if ((store.Flags(n) & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return errorType;
        }
        var classDecl = Node.None;
        var isImplements = false;
        if (kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            var heritage = store.Parent(n);
            if (store.Kind(heritage) == SyntaxKind.HeritageClause && Utilities.IsClassLike(store, store.Parent(heritage)))
            {
                classDecl = store.Parent(heritage);
                isImplements = store.Token(heritage) == SyntaxKind.ImplementsKeyword;
            }
        }
        var classType = classDecl.IsNone ? Type.None : GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(At(node, classDecl)));
        if (Utilities.IsPartOfTypeNode(store, n))
        {
            // (getTypeWithThisArgument for class heritage: C3)
            return GetTypeFromTypeNode(node);
        }
        if (Utilities.IsExpressionNode(store, n))
        {
            return GetRegularTypeOfExpression(node);
        }
        if (!classType.IsNone && !isImplements)
        {
            // A SyntaxKind.ExpressionWithTypeArguments is considered a type node, except when it occurs in the
            // extends clause of a class. We handle that case here.
            var baseTypes = GetBaseTypes(classType);
            return baseTypes.Count > 0 ? baseTypes[0] : errorType;
        }
        if (IsTypeDeclaration(node))
        {
            // In this case, we call getSymbolOfNode instead of getSymbolAtLocation because it is a declaration
            return GetDeclaredTypeOfSymbol(GetSymbolOfDeclaration(node));
        }
        if (kind == SyntaxKind.Identifier && IsTypeDeclaration(Parent(node)) && GetNameOfDeclarationRef(Parent(node)) == node)
        {
            var symbol = GetSymbolAtLocation(node);
            return !symbol.IsNone ? GetDeclaredTypeOfSymbol(symbol) : errorType;
        }
        if (kind == SyntaxKind.BindingElement)
        {
            var t = GetTypeForVariableLikeDeclaration(node, includeOptionality: true);
            return t.IsNone ? errorType : t;
        }
        if (Utilities.IsDeclaration(store, n))
        {
            // In this case, we call getSymbolOfNode instead of getSymbolAtLocation because it is a declaration
            var symbol = GetSymbolOfDeclaration(node);
            return !symbol.IsNone ? GetTypeOfSymbol(symbol) : errorType;
        }
        if (IsDeclarationNameOrImportPropertyName(store, n))
        {
            var symbol = GetSymbolAtLocation(node);
            return !symbol.IsNone ? GetTypeOfSymbol(symbol) : errorType;
        }
        if (Utilities.IsBindingPattern(store, n))
        {
            var t = GetTypeForVariableLikeDeclaration(Parent(node), includeOptionality: true);
            return t.IsNone ? errorType : t;
        }
        if (kind is SyntaxKind.Identifier or SyntaxKind.QualifiedName && IsInRightSideOfImportOrExportAssignment(store, n))
        {
            var symbol = GetSymbolAtLocation(node);
            if (!symbol.IsNone)
            {
                var declaredType = GetDeclaredTypeOfSymbol(symbol);
                return !IsErrorType(declaredType) ? declaredType : GetTypeOfSymbol(symbol);
            }
        }
        // (meta property keywords, import attributes: C5/C9)
        return errorType;
    }

    /// <summary>checker.ts getRegularTypeOfExpression.</summary>
    internal Type GetRegularTypeOfExpression(NodeRef expr)
    {
        var store = StoreOf(expr);
        if (Utilities.IsRightSideOfQualifiedNameOrPropertyAccess(store, expr.Node))
        {
            expr = Parent(expr);
        }
        return GetRegularTypeOfLiteralType(GetTypeOfExpression(expr));
    }
}
