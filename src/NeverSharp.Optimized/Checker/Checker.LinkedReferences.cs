// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (markLinkedReferences with the hints Identifier, Property, ExportAssignment,
// ExportImportEquals and ExportSpecifier; markIdentifierAliasReferenced, markPropertyAliasReferenced,
// markExportAssignmentAliasReferenced, markImportEqualsAliasReferenced, markExportSpecifierAliasReferenced,
// markAliasReferenced, markAliasSymbolAsReferenced, markExportAsReferenced, shouldMarkIdentifierAliasReferenced,
// isConstEnumOrConstEnumOnlyModule)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C9e. The alias marking is an emit concern except for its side
// effects: resolving the first identifier of an `import x = a.b` reference as a value reports the name errors tsc reports.
// Not ported (no effect under the Gate C options): the Jsx, AsyncFunction (target below ES2015) and Decorator
// (emitDecoratorMetadata) hints and the isolatedModules/preserveConstEnums exceptions.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts markLinkedReferences: references within types and declaration files never retain a JS import
    private bool IsAmbientReferenceLocation(NodeRef location)
    {
        var store = StoreOf(location);
        return (store.Flags(location.Node) & NodeFlags.Ambient) != 0 && store.Kind(location.Node) is not (SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration);
    }

    // checker.ts markLinkedReferences(location, ReferenceHint.Identifier) / markIdentifierAliasReferenced
    private void MarkIdentifierAliasReferenced(NodeRef location)
    {
        if (IsAmbientReferenceLocation(location))
        {
            return;
        }
        MarkIdentifierAliasReferencedWorker(location);
    }

    private void MarkIdentifierAliasReferencedWorker(NodeRef location)
    {
        var symbol = GetResolvedSymbol(location);
        if (!symbol.IsNone && symbol != argumentsSymbol && symbol != unknownSymbol && !IsThisInTypeQuery(StoreOf(location), location.Node))
        {
            MarkAliasReferenced(symbol, location);
        }
    }

    // checker.ts markLinkedReferences(location, ReferenceHint.Property, propSymbol, parentType) / markPropertyAliasReferenced
    // (parentType is always given at the call sites)
    private void MarkPropertyAliasReferenced(NodeRef location, SymbolRef propSymbol, Type parentType)
    {
        if (IsAmbientReferenceLocation(location))
        {
            return;
        }
        var store = StoreOf(location);
        var left = store.Kind(location.Node) == SyntaxKind.PropertyAccessExpression ? store.Expression(location.Node) : store.Left(location.Node);
        if (store.Kind(left) != SyntaxKind.Identifier || Utilities.IsThisIdentifier(store, left))
        {
            return;
        }
        var parentSymbol = GetResolvedSymbol(At(location, left));
        if (parentSymbol.IsNone || parentSymbol == unknownSymbol)
        {
            return;
        }
        // In `Foo.Bar.Baz`, 'Foo' is not referenced if 'Bar' is a const enum or a module containing only const enums.
        // (getIsolatedModules / shouldPreserveConstEnums: false under the Gate C options)
        if (IsTypeAny(parentType) || parentType == silentNeverType)
        {
            MarkAliasReferenced(parentSymbol, location);
            return;
        }
        if (!(!propSymbol.IsNone && (IsConstEnumOrConstEnumOnlyModule(propSymbol) ||
            (SymbolFlagsOf(propSymbol) & SymbolFlags.EnumMember) != 0 && store.Kind(store.Parent(location.Node)) == SyntaxKind.EnumMember)))
        {
            MarkAliasReferenced(parentSymbol, location);
        }
    }

    // checker.ts markLinkedReferences(location, ReferenceHint.ExportAssignment) / markExportAssignmentAliasReferenced
    private void MarkExportAssignmentAliasReferenced(NodeRef location)
    {
        if (IsAmbientReferenceLocation(location))
        {
            return;
        }
        var store = StoreOf(location);
        var id = store.Expression(location.Node);
        if (store.Kind(id) == SyntaxKind.Identifier)
        {
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(At(location, id), SymbolFlags.All, ignoreErrors: true, dontResolveAlias: true, location));
            if (!sym.IsNone)
            {
                MarkAliasReferenced(sym, At(location, id));
            }
        }
    }

    // checker.ts markLinkedReferences(location, ReferenceHint.ExportImportEquals) / markImportEqualsAliasReferenced
    private void MarkImportEqualsAliasReferenced(NodeRef location)
    {
        if (IsAmbientReferenceLocation(location))
        {
            return;
        }
        if (Utilities.HasSyntacticModifierUncached(StoreOf(location), location.Node, ModifierFlags.Export))
        {
            MarkExportAsReferenced(location);
        }
    }

    // checker.ts markLinkedReferences(location, ReferenceHint.ExportSpecifier) / markExportSpecifierAliasReferenced
    private void MarkExportSpecifierAliasReferenced(NodeRef location)
    {
        if (IsAmbientReferenceLocation(location))
        {
            return;
        }
        var store = StoreOf(location);
        var n = location.Node;
        var exportDeclaration = store.Parent(store.Parent(n));
        if (!store.ModuleSpecifier(exportDeclaration).IsNone || store.IsTypeOnly(n) || store.IsTypeOnly(exportDeclaration))
        {
            return;
        }
        var exportedName = !store.PropertyName(n).IsNone ? store.PropertyName(n) : store.Name(n);
        if (store.Kind(exportedName) == SyntaxKind.StringLiteral)
        {
            return; // Skip for invalid syntax like this: export { "x" }
        }
        var symbol = ResolveName(At(location, exportedName), store.Texts.GetString(store.EscapedText(exportedName))!, NodeRef.None,
            SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, null, isUse: true);
        if (!symbol.IsNone && (symbol == undefinedSymbol || symbol == globalThisSymbol ||
            (DeclarationsOf(symbol) is { Count: > 0 } declarations && IsGlobalSourceFile(GetDeclarationContainer(declarations[0])))))
        {
            // Do nothing, non-local symbol
            return;
        }
        var target = symbol.IsNone ? SymbolRef.None : (SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0 ? ResolveAlias(symbol) : symbol;
        if (target.IsNone || (GetSymbolFlags(target) & SymbolFlags.Value) != 0)
        {
            MarkExportAsReferenced(location); // marks export as used
            MarkIdentifierAliasReferencedWorker(At(location, exportedName)); // marks target of export as used
        }
    }

    // checker.ts markAliasReferenced
    private void MarkAliasReferenced(SymbolRef symbol, NodeRef location)
    {
        if (IsNonLocalAlias(SymbolFlagsOf(symbol), SymbolFlags.Value) && !Utilities.IsInTypeQuery(StoreOf(location), location.Node))
        {
            var target = ResolveAlias(symbol);
            if ((GetSymbolFlags(symbol, excludeTypeOnlyMeanings: true) & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0)
            {
                // An alias resolving to a const enum cannot be elided if (1) 'isolatedModules' is enabled
                // (because the const enum value will not be inlined), or if (2) the alias is an export
                // of a const enum declaration that will be preserved. (Neither applies to Gate C.)
                if (!IsConstEnumOrConstEnumOnlyModule(GetExportSymbolOfValueSymbolIfExported(target)))
                {
                    MarkAliasSymbolAsReferenced(symbol);
                }
            }
        }
    }

    // checker.ts markAliasSymbolAsReferenced: when an alias symbol is referenced, the entity it references is marked
    // as referenced in turn, until a non-alias or an exported entity is reached.
    private void MarkAliasSymbolAsReferenced(SymbolRef symbol)
    {
        if (AliasSymbolLinks.Get(symbol).Referenced)
        {
            return;
        }
        AliasSymbolLinks.Get(symbol).Referenced = true;
        var node = GetDeclarationOfAliasSymbol(symbol);
        if (node.IsNone)
        {
            return; // Debug.fail()
        }
        // We defer checking of the reference of an `import =` until the import itself is referenced,
        // This way a chain of imports can be elided if ultimately the final input is only used in a type
        // position.
        var store = StoreOf(node);
        if (Utilities.IsInternalModuleImportEqualsDeclaration(store, node.Node))
        {
            if ((GetSymbolFlags(ResolveSymbol(symbol)) & SymbolFlags.Value) != 0)
            {
                // import foo = <symbol>
                var left = Utilities.GetFirstIdentifier(store, store.ModuleReference(node.Node));
                MarkIdentifierAliasReferencedWorker(At(node, left));
            }
        }
    }

    // checker.ts markExportAsReferenced
    private void MarkExportAsReferenced(NodeRef node)
    {
        var symbol = GetSymbolOfDeclaration(node);
        var target = ResolveAlias(symbol);
        if (!target.IsNone)
        {
            var markAlias = target == unknownSymbol ||
                ((GetSymbolFlags(symbol, excludeTypeOnlyMeanings: true) & SymbolFlags.Value) != 0 && !IsConstEnumOrConstEnumOnlyModule(target));
            if (markAlias)
            {
                MarkAliasSymbolAsReferenced(symbol);
            }
        }
    }

    // checker.ts shouldMarkIdentifierAliasReferenced
    private static bool ShouldMarkIdentifierAliasReferenced(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        if (!parent.IsNone)
        {
            // A property access expression LHS? checkPropertyAccessExpression will handle that.
            if (store.Kind(parent) == SyntaxKind.PropertyAccessExpression && store.Expression(parent) == node)
            {
                return false;
            }
            // Next two check for an identifier inside a type only export.
            if (store.Kind(parent) == SyntaxKind.ExportSpecifier && store.IsTypeOnly(parent))
            {
                return false;
            }
            var grandparent = store.Parent(parent);
            var greatGrandparent = grandparent.IsNone ? Node.None : store.Parent(grandparent);
            if (!greatGrandparent.IsNone && store.Kind(greatGrandparent) == SyntaxKind.ExportDeclaration && store.IsTypeOnly(greatGrandparent))
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts isConstEnumOrConstEnumOnlyModule
    private bool IsConstEnumOrConstEnumOnlyModule(SymbolRef s) =>
        (SymbolFlagsOf(s) & SymbolFlags.ConstEnum) != 0 || ConstEnumOnlyModuleOf(s);
}
