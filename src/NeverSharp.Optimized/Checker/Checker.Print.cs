// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (symbolToString with the node builder's lookupSymbolChain, getSymbolChain,
// getAccessibleSymbolChain, forEachSymbolTableInScope, getContainersOfSymbol, getNameOfSymbolAsWritten and
// symbolToExpression; typeToString for the type kinds C1 creates: intrinsic, literal, enum, named object types,
// type references, arrays, unions with formatUnionTypes, intersections, type parameters)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/nodebuilder.go and internal/checker/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1: a minimal path that writes the text directly instead of
// building and printing nodes. TODO(C2): the node builder (typeToTypeNode, symbolToTypeNode, signatures, type
// literals, reuse of existing type nodes and their underlines, truncation, accessibility of aliases), and the printer.

using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // ---- symbolToString ----

    /// <summary>checker.ts symbolToString(symbol, enclosingDeclaration, meaning, flags = AllowAnyNodeKind).</summary>
    internal string SymbolToString(SymbolRef symbol, NodeRef enclosingDeclaration = default, SymbolFlags meaning = SymbolFlags.None, SymbolFormatFlags flags = SymbolFormatFlags.AllowAnyNodeKind)
    {
        var doNotIncludeSymbolChain = (flags & SymbolFormatFlags.DoNotIncludeSymbolChain) != 0;
        List<SymbolRef> chain;
        if ((SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) == 0 && !enclosingDeclaration.IsNone && !doNotIncludeSymbolChain)
        {
            chain = GetSymbolChain(symbol, meaning, endOfChain: true, enclosingDeclaration) ?? [symbol];
        }
        else
        {
            chain = [symbol];
        }
        return SymbolChainToString(chain, enclosingDeclaration);
    }

    // nodebuilder symbolToExpression / createExpressionFromSymbolChain, printed
    private string SymbolChainToString(List<SymbolRef> chain, NodeRef enclosingDeclaration)
    {
        var sb = new StringBuilder();
        for (var index = 0; index < chain.Count; index++)
        {
            var symbol = chain[index];
            var symbolName = GetNameOfSymbolAsWritten(symbol, enclosingDeclaration, index == 0);
            if (index == 0 || CanUsePropertyAccess(symbolName))
            {
                if (index > 0)
                {
                    sb.Append('.');
                }
                sb.Append(symbolName);
                continue;
            }
            var firstChar = symbolName.Length > 0 ? symbolName[0] : '\0';
            if (firstChar == '[')
            {
                symbolName = symbolName[1..^1];
                firstChar = symbolName.Length > 0 ? symbolName[0] : '\0';
            }
            sb.Append('[');
            if ((firstChar == '"' || firstChar == '\'') && (SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) == 0)
            {
                var text = symbolName.Length >= 2 ? symbolName[1..^1] : symbolName;
                var quote = firstChar;
                sb.Append(quote).Append(EscapeStringLiteral(text.Replace("\\\\", "\\", StringComparison.Ordinal), quote)).Append(quote);
            }
            else
            {
                sb.Append(symbolName);
            }
            sb.Append(']');
        }
        return sb.ToString();
    }

    private bool CanUsePropertyAccess(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }
        var firstChar = name[0];
        if (firstChar == '#')
        {
            return name.Length > 1 && Scanner.IsIdentifierStart(name[1], languageVersion);
        }
        return Scanner.IsIdentifierStart(firstChar, languageVersion);
    }

    // checker.ts getNameOfSymbolAsWritten
    private string GetNameOfSymbolAsWritten(SymbolRef symbol, NodeRef enclosingDeclaration, bool inInitialEntityName)
    {
        var declarations = DeclarationsOf(symbol);
        if (NameOf(symbol) == InternalSymbolName.Default &&
            (!inInitialEntityName || declarations is null ||
                (!enclosingDeclaration.IsNone && FindDefaultBindingContext(declarations[0]) != FindDefaultBindingContext(enclosingDeclaration))))
        {
            return "default";
        }
        if (declarations is { Count: > 0 })
        {
            var declaration = NodeRef.None;
            var name = NodeRef.None;
            foreach (var d in declarations)
            {
                var n = GetNameOfDeclarationRef(d);
                if (!n.IsNone)
                {
                    declaration = d;
                    name = n;
                    break;
                }
            }
            if (!declaration.IsNone)
            {
                // (Object.defineProperty declarations and computed names with literal name types: C9 / C3)
                return Utilities.DeclarationNameToString(StoreOf(name), name.Node);
            }
            declaration = declarations[0];
            var parent = Parent(declaration);
            if (!parent.IsNone && Kind(parent) == SyntaxKind.VariableDeclaration)
            {
                var store = StoreOf(parent);
                return Utilities.DeclarationNameToString(store, store.Name(parent.Node));
            }
            switch (Kind(declaration))
            {
                case SyntaxKind.ClassExpression:
                    return "(Anonymous class)";
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                    return "(Anonymous function)";
            }
        }
        // (name types of late-bound symbols: C3)
        return SymbolName(symbol);
    }

    // isDefaultBindingContext ancestor: a module block, or an external module source file
    private NodeRef FindDefaultBindingContext(NodeRef node)
    {
        for (var n = node; !n.IsNone; n = Parent(n))
        {
            var kind = Kind(n);
            if (kind == SyntaxKind.ModuleBlock || (kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule(StoreOf(n), n.Node, BindingOf(n.File))))
            {
                return n;
            }
        }
        return NodeRef.None;
    }

    // nodebuilder lookupSymbolChainWorker's getSymbolChain
    private List<SymbolRef>? GetSymbolChain(SymbolRef symbol, SymbolFlags meaning, bool endOfChain, NodeRef enclosingDeclaration, int depth = 0)
    {
        if (depth > 32)
        {
            return null;
        }
        var accessibleSymbolChain = GetAccessibleSymbolChain(symbol, enclosingDeclaration, meaning);
        if (accessibleSymbolChain is null)
        {
            // Go up and add our parent.
            var parents = GetContainersOfSymbol(symbol, enclosingDeclaration);
            foreach (var parent in parents)
            {
                var parentChain = GetSymbolChain(parent, GetQualifiedLeftMeaning(meaning), endOfChain: false, enclosingDeclaration, depth + 1);
                if (parentChain is not null)
                {
                    var parentExports = ExportsOf(parent);
                    if (parentExports is not null && parentExports.Has(InternalSymbolName.ExportEquals) && ResolveSymbol(parentExports.Get(InternalSymbolName.ExportEquals)) == ResolveSymbol(symbol))
                    {
                        accessibleSymbolChain = parentChain;
                        break;
                    }
                    accessibleSymbolChain = [.. parentChain, symbol];
                    break;
                }
            }
        }
        if (accessibleSymbolChain is not null)
        {
            return accessibleSymbolChain;
        }
        if (endOfChain || (SymbolFlagsOf(symbol) & (SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) == 0)
        {
            // If this is the last part of outputting the symbol, always output. The cases this can happen are when this is
            // a type literal or an object literal symbol, or when we don't have an accessible chain
            if (!endOfChain && (DeclarationsOf(symbol) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
            {
                return null;
            }
            return [symbol];
        }
        return null;
    }

    private static SymbolFlags GetQualifiedLeftMeaning(SymbolFlags rightMeaning) =>
        // If we are looking in value space, the parent meaning is value, other wise it is namespace
        rightMeaning == SymbolFlags.Value ? SymbolFlags.Value : SymbolFlags.Namespace;

    // utilities.ts hasNonGlobalAugmentationExternalModuleSymbol: a module with a string literal name or an external module file
    private bool HasNonGlobalAugmentationExternalModuleSymbol(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var kind = store.Kind(declaration.Node);
        if (kind == SyntaxKind.ModuleDeclaration && store.Kind(store.Name(declaration.Node)) == SyntaxKind.StringLiteral)
        {
            return true;
        }
        return kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule(store, declaration.Node, BindingOf(declaration.File));
    }

    // checker.ts getContainersOfSymbol (re-export and object literal variable containers: C2)
    private List<SymbolRef> GetContainersOfSymbol(SymbolRef symbol, NodeRef enclosingDeclaration)
    {
        var container = GetParentOfSymbol(symbol);
        // Type parameters end up in the `members` lists but are not externally visible
        if (!container.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) == 0)
        {
            return [container];
        }
        var candidates = new List<SymbolRef>();
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            var store = StoreOf(d);
            if (!Utilities.IsAmbientModule(store, d.Node))
            {
                var parent = Parent(d);
                if (!parent.IsNone)
                {
                    // direct children of a module
                    if (HasNonGlobalAugmentationExternalModuleSymbol(parent))
                    {
                        candidates.Add(GetSymbolOfDeclaration(parent));
                        continue;
                    }
                    // export ='d member of an ambient module
                    if (Kind(parent) == SyntaxKind.ModuleBlock && !Parent(parent).IsNone && ResolveExternalModuleSymbol(GetSymbolOfDeclaration(Parent(parent))) == symbol)
                    {
                        candidates.Add(GetSymbolOfDeclaration(Parent(parent)));
                    }
                }
            }
        }
        var result = new List<SymbolRef>();
        foreach (var candidate in candidates)
        {
            if (!candidate.IsNone && !GetAliasForSymbolInContainer(candidate, symbol).IsNone && !result.Contains(candidate))
            {
                result.Add(candidate);
            }
        }
        return result;
    }

    // checker.ts getAliasForSymbolInContainer
    private SymbolRef GetAliasForSymbolInContainer(SymbolRef container, SymbolRef symbol)
    {
        if (container == GetParentOfSymbol(symbol))
        {
            // fast path, `symbol` is either already the alias or isn't aliased
            return symbol;
        }
        // Check if container is a thing with an `export=` which points directly at `symbol`, and if so, return
        // the container itself as the alias for the symbol
        var exportEquals = ExportsOf(container)?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
        if (!exportEquals.IsNone && ResolveSymbol(exportEquals) == ResolveSymbol(symbol))
        {
            return container;
        }
        var exports = GetExportsOfSymbol(container);
        var quick = exports.Get(NameOf(symbol));
        if (!quick.IsNone && ResolveSymbol(quick) == ResolveSymbol(symbol))
        {
            return quick;
        }
        for (var i = 0; i < exports.Count; i++)
        {
            if (ResolveSymbol(exports.ValueAt(i)) == ResolveSymbol(symbol))
            {
                return exports.ValueAt(i);
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getAccessibleSymbolChain (meaning: the name walks pass undefined, so no qualification is needed)
    private List<SymbolRef>? GetAccessibleSymbolChain(SymbolRef symbol, NodeRef enclosingDeclaration, SymbolFlags meaning)
    {
        if (symbol.IsNone || IsPropertyOrMethodDeclarationSymbol(symbol))
        {
            return null;
        }
        var visited = new List<SymTable>();
        return ForEachSymbolTableInScope(enclosingDeclaration, (table, ignoreQualification) => TrySymbolTableForChain(symbol, table, ignoreQualification, meaning, visited, enclosingDeclaration));
    }

    private List<SymbolRef>? TrySymbolTableForChain(SymbolRef symbol, SymTable symbols, bool ignoreQualification, SymbolFlags meaning, List<SymTable> visited, NodeRef enclosingDeclaration)
    {
        if (visited.Contains(symbols))
        {
            return null;
        }
        visited.Add(symbols);
        try
        {
            // If symbol is directly available by its name in the symbol table
            var direct = symbols.Get(NameOf(symbol));
            if (!direct.IsNone && IsAccessibleForChain(symbol, direct, SymbolRef.None, ignoreQualification, meaning, enclosingDeclaration))
            {
                return [symbol];
            }
            // Check if symbol is any of the aliases in scope (aliases resolved to the symbol)
            for (var i = 0; i < symbols.Count; i++)
            {
                var symbolFromSymbolTable = symbols.ValueAt(i);
                var name = symbols.NameAt(i);
                var flags = SymbolFlagsOf(symbolFromSymbolTable);
                if ((flags & SymbolFlags.Alias) != 0 && name != InternalSymbolName.ExportEquals && name != InternalSymbolName.Default
                    && (ignoreQualification || GetDeclarationOfKind(symbolFromSymbolTable, SyntaxKind.ExportSpecifier).IsNone))
                {
                    var resolvedImportedSymbol = ResolveAlias(symbolFromSymbolTable);
                    if (IsAccessibleForChain(symbol, symbolFromSymbolTable, resolvedImportedSymbol, ignoreQualification, meaning, enclosingDeclaration))
                    {
                        return [symbolFromSymbolTable];
                    }
                }
                if (name == NameOf(symbol) && !ExportSymbolOf(symbolFromSymbolTable).IsNone)
                {
                    if (IsAccessibleForChain(symbol, GetMergedSymbol(ExportSymbolOf(symbolFromSymbolTable)), SymbolRef.None, ignoreQualification, meaning, enclosingDeclaration))
                    {
                        return [symbol];
                    }
                }
            }
            return null;
        }
        finally
        {
            visited.RemoveAt(visited.Count - 1);
        }
    }

    private bool IsAccessibleForChain(SymbolRef symbol, SymbolRef symbolFromSymbolTable, SymbolRef resolvedAliasSymbol, bool ignoreQualification, SymbolFlags meaning, NodeRef enclosingDeclaration)
    {
        var target = !resolvedAliasSymbol.IsNone ? resolvedAliasSymbol : symbolFromSymbolTable;
        if (!(symbol == target || GetMergedSymbol(symbol) == GetMergedSymbol(target)))
        {
            return false;
        }
        // if the symbolFromSymbolTable is not external module (it could be if it was determined as ambient external module and would be in globals table)
        if ((DeclarationsOf(symbolFromSymbolTable) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
        {
            return false;
        }
        return ignoreQualification || !NeedsQualification(GetMergedSymbol(symbolFromSymbolTable), enclosingDeclaration, meaning);
    }

    // checker.ts needsQualification
    private bool NeedsQualification(SymbolRef symbol, NodeRef enclosingDeclaration, SymbolFlags meaning)
    {
        var qualify = false;
        var name = NameOf(symbol);
        ForEachSymbolTableInScope<object>(enclosingDeclaration, (symbolTable, _) =>
        {
            // If symbol of this name is not available in the symbol table we are ok
            var symbolFromSymbolTable = GetMergedSymbol(symbolTable.Get(name));
            if (symbolFromSymbolTable.IsNone)
            {
                // Continue to the next symbol table
                return null;
            }
            // If the symbol with this name is present it should refer to the symbol
            if (symbolFromSymbolTable == symbol)
            {
                // No need to qualify
                return true;
            }
            // Qualify if the symbol from symbol table has same meaning as expected
            var shouldResolveAlias = (SymbolFlagsOf(symbolFromSymbolTable) & SymbolFlags.Alias) != 0 && GetDeclarationOfKind(symbolFromSymbolTable, SyntaxKind.ExportSpecifier).IsNone;
            symbolFromSymbolTable = shouldResolveAlias ? ResolveAlias(symbolFromSymbolTable) : symbolFromSymbolTable;
            var flags = shouldResolveAlias ? GetSymbolFlags(symbolFromSymbolTable) : SymbolFlagsOf(symbolFromSymbolTable);
            if ((flags & meaning) != 0)
            {
                qualify = true;
                return true;
            }
            // Continue to the next symbol table
            return null;
        });
        return qualify;
    }

    private bool IsPropertyOrMethodDeclarationSymbol(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        if (declarations is not { Count: > 0 })
        {
            return false;
        }
        foreach (var declaration in declarations)
        {
            switch (Kind(declaration))
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }

    // checker.ts forEachSymbolTableInScope
    private T? ForEachSymbolTableInScope<T>(NodeRef enclosingDeclaration, Func<SymTable, bool, T?> callback)
        where T : class
    {
        for (var location = enclosingDeclaration; !location.IsNone; location = Parent(location))
        {
            var store = StoreOf(location);
            var n = location.Node;
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            if (Utilities.CanHaveLocals(store, n) && !IsGlobalSourceFile(location))
            {
                var locals = LocalsOf(location);
                if (locals is not null)
                {
                    var result = callback(locals, false);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
            switch (store.Kind(n))
            {
                case SyntaxKind.SourceFile:
                case SyntaxKind.ModuleDeclaration:
                {
                    if (store.Kind(n) == SyntaxKind.SourceFile && !Utilities.IsExternalOrCommonJsModule(store, n, BindingOf(location.File)))
                    {
                        break;
                    }
                    var sym = GetSymbolOfDeclaration(location);
                    var result = callback((sym.IsNone ? null : ExportsOf(sym)) ?? emptySymbols, false);
                    if (result is not null)
                    {
                        return result;
                    }
                    break;
                }
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                {
                    // Type parameters are bound into `members` lists so they can aid in filtering out private members of types
                    SymTable? table = null;
                    var members = MembersOf(GetSymbolOfDeclaration(location));
                    if (members is not null)
                    {
                        for (var i = 0; i < members.Count; i++)
                        {
                            if ((SymbolFlagsOf(members.ValueAt(i)) & (SymbolFlags.Type & ~SymbolFlags.Assignment)) != 0)
                            {
                                (table ??= new SymTable()).Set(members.NameAt(i), members.ValueAt(i));
                            }
                        }
                    }
                    if (table is not null)
                    {
                        var result = callback(table, false);
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                    break;
                }
            }
        }
        return callback(globals, false);
    }

    // ---- typeToString (minimal, see the file header) ----

    private readonly HashSet<Type> printingTypes = [];

    /// <summary>checker.ts typeToString (C1: the minimal path; flags beyond the array form are ignored).</summary>
    internal string TypeToString(Type type, NodeRef enclosingDeclaration = default, TypeFormatFlags flags = TypeFormatFlags.None) =>
        TypeToText(type, enclosingDeclaration, inTypeAlias: false, writeArrayAsGeneric: (flags & TypeFormatFlags.WriteArrayAsGenericType) != 0, depth: 0);

    /// <summary>
    /// typeToTypeNode(type, enclosingDeclaration, flags) printed by typeWriter.ts' underlining printer: the type text and
    /// its underline (^ under fully synthesized nodes; C1 writes synthesized nodes only).
    /// </summary>
    internal (string Text, string Underline) TypeToBaselineText(Type type, NodeRef enclosingDeclaration, bool inTypeAlias)
    {
        string marked;
        markReuse = true;
        try
        {
            marked = TypeToText(type, enclosingDeclaration, inTypeAlias, writeArrayAsGeneric: false, depth: 0);
        }
        finally
        {
            markReuse = false;
        }
        var text = new StringBuilder(marked.Length);
        var underline = new StringBuilder(marked.Length);
        var reused = 0;
        foreach (var c in marked)
        {
            if (c == ReuseStart)
            {
                reused++;
            }
            else if (c == ReuseEnd)
            {
                reused--;
            }
            else
            {
                text.Append(c);
                underline.Append(reused > 0 ? ' ' : '^');
            }
        }
        return (text.ToString(), underline.ToString());
    }

    // The underlining printer writes spaces under nodes the node builder reused from the source (parameter names,
    // annotations); TypeToText brackets such text with these private-use markers while markReuse is set.
    private const char ReuseStart = (char)0xE000;
    private const char ReuseEnd = (char)0xE001;
    private bool markReuse;

    private string Reused(string text) => markReuse ? ReuseStart + text + ReuseEnd : text;

    // syntacticNodeBuilder reuse of an existing annotation (C1: annotations of simple shapes whose type is the
    // serialized type; TODO(C2): tryReuseExistingTypeNode in full)
    private string? TryReuseAnnotation(NodeRef annotation, Type type, bool optional)
    {
        if (annotation.IsNone || !IsSimpleReusableTypeNode(StoreOf(annotation), annotation.Node))
        {
            return null;
        }
        var fromNode = GetTypeFromTypeNode(annotation);
        // canReuseTypeNodeAnnotation: annotations resolving to error types are always reused
        if (!IsErrorType(fromNode) && fromNode != type && !(optional && RemoveUndefinedForOptional(type) == fromNode))
        {
            return null;
        }
        var store = StoreOf(annotation);
        var text = PrintReusedTypeNode(store, Utilities.RootOf(store, annotation.Node), annotation.Node);
        return Reused(text);
    }

    // The printer's output for a reused type node of the shapes IsSimpleReusableTypeNode accepts; the node builder's
    // visitor marks reused type literals single-line, so members are written on one line with "; " separators.
    private static string PrintReusedTypeNode(NodeStore store, Node root, Node node)
    {
        string Source(Node n) => Utilities.GetSourceTextOfNodeFromSourceFile(store, root, n);
        string List(NodeList list, string separator)
        {
            var parts = new List<string>();
            foreach (var item in store.Nodes(list))
            {
                parts.Add(PrintReusedTypeNode(store, root, item));
            }
            return string.Join(separator, parts);
        }
        string Parameters(Node owner)
        {
            var parts = new List<string>();
            foreach (var parameter in store.Nodes(store.Parameters(owner)))
            {
                parts.Add((store.DotDotDotToken(parameter).IsNone ? "" : "...") + Source(store.Name(parameter)) + (store.QuestionToken(parameter).IsNone ? "" : "?") +
                    (store.Type(parameter).IsNone ? "" : ": " + PrintReusedTypeNode(store, root, store.Type(parameter))));
            }
            return string.Join(", ", parts);
        }
        switch (store.Kind(node))
        {
            case SyntaxKind.TypeReference:
            {
                var typeArguments = store.TypeArguments(node);
                return Source(store.TypeName(node)) + (typeArguments.IsNone ? "" : "<" + List(typeArguments, ", ") + ">");
            }
            case SyntaxKind.ArrayType:
                return PrintReusedTypeNode(store, root, store.ElementType(node)) + "[]";
            case SyntaxKind.ParenthesizedType:
                return "(" + PrintReusedTypeNode(store, root, store.Type(node)) + ")";
            case SyntaxKind.TypePredicate:
                return (store.AssertsModifier(node).IsNone ? "" : "asserts ") + Source(store.ParameterName(node)) +
                    (store.Type(node).IsNone ? "" : " is " + PrintReusedTypeNode(store, root, store.Type(node)));
            case SyntaxKind.TypeOperator:
                return "readonly " + PrintReusedTypeNode(store, root, store.Type(node));
            case SyntaxKind.UnionType:
                return List(store.Types(node), " | ");
            case SyntaxKind.FunctionType:
            {
                var typeParameters = store.TypeParameters(node);
                var typeParameterText = "";
                if (!typeParameters.IsNone && store.Length(typeParameters) > 0)
                {
                    var parts = new List<string>();
                    foreach (var typeParameter in store.Nodes(typeParameters))
                    {
                        var constraint = store.Constraint(typeParameter);
                        parts.Add(Source(store.Name(typeParameter)) + (constraint.IsNone ? "" : " extends " + PrintReusedTypeNode(store, root, constraint)));
                    }
                    typeParameterText = "<" + string.Join(", ", parts) + ">";
                }
                return typeParameterText + "(" + Parameters(node) + ") => " + PrintReusedTypeNode(store, root, store.Type(node));
            }
            case SyntaxKind.TypeLiteral:
            {
                var members = store.Nodes(store.Members(node));
                if (members.Length == 0)
                {
                    return "{}";
                }
                var sb = new StringBuilder("{ ");
                foreach (var member in members)
                {
                    if (store.Kind(member) == SyntaxKind.CallSignature)
                    {
                        sb.Append('(').Append(Parameters(member)).Append("): ").Append(PrintReusedTypeNode(store, root, store.Type(member))).Append("; ");
                        continue;
                    }
                    if (store.Kind(member) == SyntaxKind.IndexSignature)
                    {
                        sb.Append(Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Readonly) ? "readonly " : "")
                            .Append('[').Append(Parameters(member)).Append("]: ").Append(PrintReusedTypeNode(store, root, store.Type(member))).Append("; ");
                        continue;
                    }
                    var optional = store.QuestionToken(member).IsNone ? "" : "?";
                    if (store.Kind(member) == SyntaxKind.PropertySignature)
                    {
                        sb.Append(Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Readonly) ? "readonly " : "")
                            .Append(Source(store.Name(member))).Append(optional).Append(": ").Append(PrintReusedTypeNode(store, root, store.Type(member))).Append("; ");
                    }
                    else
                    {
                        sb.Append(Source(store.Name(member))).Append(optional).Append('(').Append(Parameters(member)).Append("): ")
                            .Append(PrintReusedTypeNode(store, root, store.Type(member))).Append("; ");
                    }
                }
                return sb.Append('}').ToString();
            }
            default:
                return Source(node);
        }
    }

    private static bool IsSimpleReusableTypeNode(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.ObjectKeyword:
                return true;
            case SyntaxKind.TypeReference:
                var typeArguments = store.TypeArguments(node);
                var typeName = store.TypeName(node);
                while (store.Kind(typeName) == SyntaxKind.QualifiedName)
                {
                    typeName = store.Left(typeName);
                }
                if (store.Kind(typeName) != SyntaxKind.Identifier)
                {
                    return false;
                }
                foreach (var argument in store.Nodes(typeArguments))
                {
                    if (!IsSimpleReusableTypeNode(store, argument))
                    {
                        return false;
                    }
                }
                return true;
            case SyntaxKind.ArrayType:
                return IsSimpleReusableTypeNode(store, store.ElementType(node));
            case SyntaxKind.ParenthesizedType:
                return IsSimpleReusableTypeNode(store, store.Type(node));
            case SyntaxKind.TypePredicate:
                return store.Type(node).IsNone || IsSimpleReusableTypeNode(store, store.Type(node));
            case SyntaxKind.TypeOperator:
                return store.Operator(node) == SyntaxKind.ReadonlyKeyword && IsSimpleReusableTypeNode(store, store.Type(node));
            case SyntaxKind.LiteralType:
                return store.Kind(store.Literal(node)) is SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword;
            case SyntaxKind.UnionType:
                foreach (var member in store.Nodes(store.Types(node)))
                {
                    if (!IsSimpleReusableTypeNode(store, member))
                    {
                        return false;
                    }
                }
                return true;
            case SyntaxKind.FunctionType:
                if (!IsSimpleReusableTypeNode(store, store.Type(node)))
                {
                    return false;
                }
                foreach (var typeParameter in store.Nodes(store.TypeParameters(node)))
                {
                    var constraint = store.Constraint(typeParameter);
                    if (!store.Default(typeParameter).IsNone || !store.Modifiers(typeParameter).IsNone || (!constraint.IsNone && !IsSimpleReusableTypeNode(store, constraint)))
                    {
                        return false;
                    }
                }
                return HasSimpleReusableParameters(store, node);
            case SyntaxKind.TypeLiteral:
                foreach (var member in store.Nodes(store.Members(node)))
                {
                    if (store.Kind(member) is SyntaxKind.CallSignature or SyntaxKind.IndexSignature)
                    {
                        if (!store.TypeParameters(member).IsNone || store.Type(member).IsNone || !IsSimpleReusableTypeNode(store, store.Type(member)) || !HasSimpleReusableParameters(store, member) ||
                            (Utilities.GetSyntacticModifierFlagsNoCache(store, member) & ~ModifierFlags.Readonly) != 0)
                        {
                            return false;
                        }
                        continue;
                    }
                    var memberName = store.Name(member);
                    if (memberName.IsNone || store.Kind(memberName) is not (SyntaxKind.Identifier or SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral))
                    {
                        return false;
                    }
                    switch (store.Kind(member))
                    {
                        case SyntaxKind.PropertySignature:
                            var memberType = store.Type(member);
                            if (memberType.IsNone || !IsSimpleReusableTypeNode(store, memberType) || (Utilities.GetSyntacticModifierFlagsNoCache(store, member) & ~ModifierFlags.Readonly) != 0)
                            {
                                return false;
                            }
                            break;
                        case SyntaxKind.MethodSignature:
                            if (!store.TypeParameters(member).IsNone || store.Type(member).IsNone || !IsSimpleReusableTypeNode(store, store.Type(member)) || !HasSimpleReusableParameters(store, member))
                            {
                                return false;
                            }
                            break;
                        default:
                            return false;
                    }
                }
                return true;
            default:
                return false;
        }
    }

    private static bool HasSimpleReusableParameters(NodeStore store, Node owner)
    {
        foreach (var parameter in store.Nodes(store.Parameters(owner)))
        {
            var parameterType = store.Type(parameter);
            if (store.Kind(store.Name(parameter)) != SyntaxKind.Identifier || parameterType.IsNone || !IsSimpleReusableTypeNode(store, parameterType) ||
                !store.Modifiers(parameter).IsNone || !store.Initializer(parameter).IsNone)
            {
                return false;
            }
        }
        return true;
    }

    private string TypeToText(Type type, NodeRef enclosingDeclaration, bool inTypeAlias, bool writeArrayAsGeneric, int depth)
    {
        if (depth > 8)
        {
            return "...";
        }
        var flags = Types.Flags(type);
        var record = Types.Record(type);
        var objectFlags = record.ObjectFlags;
        if ((flags & TypeFlags.Any) != 0)
        {
            if (!record.AliasSymbol.IsNone)
            {
                return SymbolToTypeName(record.AliasSymbol, enclosingDeclaration) + TypeArgumentsText(record.AliasTypeArguments, enclosingDeclaration, depth);
            }
            return type == intrinsicMarkerType ? "intrinsic" : "any";
        }
        if ((flags & TypeFlags.Unknown) != 0)
        {
            return "unknown";
        }
        if ((flags & TypeFlags.String) != 0)
        {
            return "string";
        }
        if ((flags & TypeFlags.Number) != 0)
        {
            return "number";
        }
        if ((flags & TypeFlags.BigInt) != 0)
        {
            return "bigint";
        }
        if ((flags & TypeFlags.Boolean) != 0 && record.AliasSymbol.IsNone)
        {
            return "boolean";
        }
        if ((flags & TypeFlags.EnumLike) != 0)
        {
            if ((flags & TypeFlags.EnumLiteral) != 0 && (flags & TypeFlags.Union) == 0)
            {
                var parentSymbol = GetParentOfSymbol(record.Symbol);
                var parentName = SymbolToTypeName(parentSymbol, enclosingDeclaration);
                var enumType = GetDeclaredTypeOfSymbol(parentSymbol);
                if (enumType == type)
                {
                    return parentName;
                }
                var memberName = SymbolName(record.Symbol);
                return CanUsePropertyAccess(memberName) ? parentName + "." + memberName : parentName + "[" + Quote(memberName) + "]";
            }
            return SymbolToTypeName(record.Symbol, enclosingDeclaration);
        }
        if ((flags & TypeFlags.StringLiteral) != 0)
        {
            return Quote((string)Types.Literal(type).Value!);
        }
        if ((flags & TypeFlags.NumberLiteral) != 0)
        {
            var value = (double)Types.Literal(type).Value!;
            return value < 0 || (value == 0 && double.IsNegative(value)) ? "-" + JsNumber.ToString(-value) : JsNumber.ToString(value);
        }
        if ((flags & TypeFlags.BigIntLiteral) != 0)
        {
            return (string)Types.Literal(type).Value! + "n";
        }
        if ((flags & TypeFlags.BooleanLiteral) != 0)
        {
            return IntrinsicNameOf(type);
        }
        if ((flags & TypeFlags.UniqueESSymbol) != 0)
        {
            return "unique symbol";
        }
        if ((flags & TypeFlags.ESSymbol) != 0)
        {
            return "symbol";
        }
        if ((flags & TypeFlags.Void) != 0)
        {
            return "void";
        }
        if ((flags & TypeFlags.Undefined) != 0)
        {
            return "undefined";
        }
        if ((flags & TypeFlags.Null) != 0)
        {
            return "null";
        }
        if ((flags & TypeFlags.Never) != 0)
        {
            return "never";
        }
        if ((flags & TypeFlags.NonPrimitive) != 0)
        {
            return "object";
        }
        if ((flags & TypeFlags.TypeParameter) != 0)
        {
            if (Types.TypeParameter(type).IsThisType)
            {
                return "this";
            }
            return SymbolName(record.Symbol);
        }
        if (!inTypeAlias && !record.AliasSymbol.IsNone)
        {
            return SymbolToTypeName(record.AliasSymbol, enclosingDeclaration) + TypeArgumentsText(record.AliasTypeArguments, enclosingDeclaration, depth);
        }
        if ((flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0)
        {
            var source = type;
            if ((flags & TypeFlags.Union) != 0 && !Types.Union(type).Origin.IsNone)
            {
                source = Types.Union(type).Origin;
            }
            var members = Types.Elements(Types.Union(source).Types).ToArray();
            var types = (Types.Flags(source) & TypeFlags.Union) != 0 ? FormatUnionTypes(members) : [.. members];
            if (types.Count == 1)
            {
                return TypeToText(types[0], enclosingDeclaration, false, writeArrayAsGeneric, depth + 1);
            }
            var separator = (Types.Flags(source) & TypeFlags.Union) != 0 ? " | " : " & ";
            var sb = new StringBuilder();
            for (var i = 0; i < types.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(separator);
                }
                var memberText = TypeToText(types[i], enclosingDeclaration, false, writeArrayAsGeneric, depth + 1);
                var memberFlags = Types.Flags(types[i]);
                // union members that are function types or intersections are parenthesized by the printer
                if ((Types.Flags(source) & TypeFlags.Union) != 0 && (memberFlags & TypeFlags.Intersection) != 0 && Types.Record(types[i]).AliasSymbol.IsNone)
                {
                    memberText = "(" + memberText + ")";
                }
                else if (memberText.Contains("=>", StringComparison.Ordinal) && !memberText.StartsWith('{'))
                {
                    memberText = "(" + memberText + ")";
                }
                sb.Append(memberText);
            }
            return sb.ToString();
        }
        if ((flags & TypeFlags.Object) != 0)
        {
            if ((objectFlags & ObjectFlags.Reference) != 0 && !Types.Object(type).Target.IsNone)
            {
                var target = Types.Object(type).Target;
                var typeArguments = GetTypeArguments(type).ToArray();
                if (!writeArrayAsGeneric && (target == globalArrayType || target == globalReadonlyArrayType) && typeArguments.Length == 1)
                {
                    var elementText = TypeToText(typeArguments[0], enclosingDeclaration, false, false, depth + 1);
                    var elementFlags = Types.Flags(typeArguments[0]);
                    if ((elementFlags & (TypeFlags.Union | TypeFlags.Intersection)) != 0 && Types.Record(typeArguments[0]).AliasSymbol.IsNone && (elementFlags & TypeFlags.Boolean) == 0
                        || elementText.Contains("=>", StringComparison.Ordinal))
                    {
                        elementText = "(" + elementText + ")";
                    }
                    return (target == globalReadonlyArrayType ? "readonly " : "") + elementText + "[]";
                }
                var targetData = Types.Object(target);
                var name = SymbolToTypeName(record.Symbol, enclosingDeclaration);
                var outerCount = targetData.OuterTypeParameters.Count;
                var local = typeArguments.Length > outerCount ? typeArguments[outerCount..] : [];
                // the `this` argument of a class reference is not written
                if (local.Length == 0)
                {
                    return name;
                }
                var localCount = targetData.LocalTypeParameters.Count;
                if (local.Length > localCount)
                {
                    local = local[..localCount];
                }
                return local.Length == 0 ? name : name + "<" + string.Join(", ", Array.ConvertAll(local, t => TypeToText(t, enclosingDeclaration, false, false, depth + 1))) + ">";
            }
            if ((objectFlags & ObjectFlags.ClassOrInterface) != 0)
            {
                return SymbolToTypeName(record.Symbol, enclosingDeclaration);
            }
            var symbol = record.Symbol;
            if (!symbol.IsNone)
            {
                var symbolFlags = SymbolFlagsOf(symbol);
                if ((symbolFlags & SymbolFlags.Class) != 0 || (symbolFlags & (SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0)
                {
                    var declarations = DeclarationsOf(symbol);
                    if (declarations is { Count: > 0 } && Kind(declarations[0]) == SyntaxKind.ModuleDeclaration && declarations.Exists(HasNonGlobalAugmentationExternalModuleSymbol))
                    {
                        // symbolToTypeNode: an import type for a module (getSpecifierForModuleSymbol of an ambient module: its name)
                        var moduleName = NameOf(symbol);
                        return "typeof import(" + (moduleName.StartsWith('"') ? moduleName : Quote(moduleName)) + ")";
                    }
                    return "typeof " + SymbolToString(symbol, enclosingDeclaration, SymbolFlags.Value);
                }
                if (printingTypes.Contains(type))
                {
                    // createAnonymousTypeNode: a recursive reference to a type being written
                    return (symbolFlags & (SymbolFlags.Function | SymbolFlags.Method)) != 0 ? "typeof " + SymbolToString(symbol, enclosingDeclaration, SymbolFlags.Value) : "...";
                }
            }
            printingTypes.Add(type);
            try
            {
                return ObjectTypeLiteralText(type, enclosingDeclaration, depth);
            }
            finally
            {
                printingTypes.Remove(type);
            }
        }
        // TODO(C2/C3): template literal, string mapping, index, indexed access, conditional, substitution types
        return "any";
    }

    // checker.ts formatUnionTypes
    private List<Type> FormatUnionTypes(Type[] types)
    {
        var result = new List<Type>();
        TypeFlags flags = 0;
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            flags |= Types.Flags(t);
            if ((Types.Flags(t) & TypeFlags.Nullable) == 0)
            {
                var baseType = (Types.Flags(t) & TypeFlags.BooleanLiteral) != 0 ? booleanType : GetBaseTypeOfEnumLikeType(t);
                if ((Types.Flags(baseType) & TypeFlags.Union) != 0)
                {
                    var baseTypes = Types.Elements(Types.Union(baseType).Types);
                    var count = baseTypes.Length;
                    if (i + count <= types.Length && GetRegularTypeOfLiteralType(types[i + count - 1]) == GetRegularTypeOfLiteralType(baseTypes[count - 1]))
                    {
                        result.Add(baseType);
                        i += count - 1;
                        continue;
                    }
                }
                result.Add(t);
            }
        }
        if ((flags & TypeFlags.Null) != 0)
        {
            result.Add(nullType);
        }
        if ((flags & TypeFlags.Undefined) != 0)
        {
            result.Add(undefinedType);
        }
        return result;
    }

    private string TypeArgumentsText(TypeList typeArguments, NodeRef enclosingDeclaration, int depth)
    {
        if (typeArguments.Count == 0)
        {
            return "";
        }
        var parts = new List<string>();
        foreach (var t in Types.Elements(typeArguments).ToArray())
        {
            parts.Add(TypeToText(t, enclosingDeclaration, false, false, depth + 1));
        }
        return "<" + string.Join(", ", parts) + ">";
    }

    // symbolToTypeNode for a named type: the entity name of the symbol's chain with meaning Type
    private string SymbolToTypeName(SymbolRef symbol, NodeRef enclosingDeclaration) =>
        SymbolToString(symbol, enclosingDeclaration, SymbolFlags.Type);

    // createTypeNodeFromObjectType: the members of an anonymous object type as a type literal, or a single signature as a
    // function type (TODO(C2): the node builder's signature and member serialization, optionality and modifiers)
    private string ObjectTypeLiteralText(Type type, NodeRef enclosingDeclaration, int depth)
    {
        var resolved = ResolveStructuredTypeMembers(type);
        var callSignatures = resolved.CallSignatures!;
        var constructSignatures = resolved.ConstructSignatures!;
        var properties = resolved.Properties!;
        if (properties.Count == 0 && resolved.IndexInfos!.Count == 0)
        {
            if (callSignatures.Count == 0 && constructSignatures.Count == 0)
            {
                return "{}";
            }
            if (callSignatures.Count == 1 && constructSignatures.Count == 0)
            {
                return SignatureText(callSignatures[0], enclosingDeclaration, depth, arrow: true, construct: false);
            }
            if (constructSignatures.Count == 1 && callSignatures.Count == 0)
            {
                return SignatureText(constructSignatures[0], enclosingDeclaration, depth, arrow: true, construct: true);
            }
        }
        var sb = new StringBuilder("{ ");
        foreach (var signature in callSignatures)
        {
            sb.Append(SignatureText(signature, enclosingDeclaration, depth, arrow: false, construct: false)).Append("; ");
        }
        foreach (var signature in constructSignatures)
        {
            sb.Append("new ").Append(SignatureText(signature, enclosingDeclaration, depth, arrow: false, construct: false)).Append("; ");
        }
        foreach (var info in resolved.IndexInfos!)
        {
            sb.Append(info.IsReadonly ? "readonly " : "").Append("[key: ").Append(TypeToText(info.KeyType, enclosingDeclaration, false, false, depth + 1))
                .Append("]: ").Append(TypeToText(info.Type, enclosingDeclaration, false, false, depth + 1)).Append("; ");
        }
        foreach (var property in properties)
        {
            var propertyFlags = SymbolFlagsOf(property);
            var name = GetPropertyNameText(property);
            if ((propertyFlags & SymbolFlags.Method) != 0)
            {
                foreach (var signature in GetSignaturesOfSymbol(property))
                {
                    sb.Append(name).Append((propertyFlags & SymbolFlags.Optional) != 0 ? "?" : "").Append(SignatureText(signature, enclosingDeclaration, depth, arrow: false, construct: false)).Append("; ");
                }
                continue;
            }
            var propertyType = GetTypeOfSymbol(property);
            var optional = (propertyFlags & SymbolFlags.Optional) != 0;
            if (optional)
            {
                propertyType = RemoveUndefinedForOptional(propertyType);
            }
            var propertyDeclaration = ValueDeclarationOf(property);
            string? reusedType = null;
            if (!propertyDeclaration.IsNone && Kind(propertyDeclaration) is SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration)
            {
                reusedType = TryReuseAnnotation(At(propertyDeclaration, StoreOf(propertyDeclaration).Type(propertyDeclaration.Node)), propertyType, false);
            }
            sb.Append(IsReadonlySymbol(property) ? "readonly " : "").Append(name).Append(optional ? "?" : "").Append(": ")
                .Append(reusedType ?? TypeToText(propertyType, enclosingDeclaration, false, false, depth + 1)).Append("; ");
        }
        sb.Append('}');
        return sb.ToString();
    }

    // nodebuilder getPropertyNameNodeForSymbol / createPropertyNameNodeForIdentifierOrLiteral (name types of late-bound
    // members: C3)
    private string GetPropertyNameText(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        var stringNamed = declarations is { Count: > 0 } && declarations.TrueForAll(IsStringNamed);
        var singleQuote = declarations is { Count: > 0 } && declarations.TrueForAll(IsSingleQuotedStringNamed);
        var isMethod = (SymbolFlagsOf(symbol) & SymbolFlags.Method) != 0;
        var rawName = SymbolName(symbol);
        var isMethodNamedNew = isMethod && rawName == "new";
        if (!isMethodNamedNew && Scanner.IsIdentifierText(rawName, languageVersion))
        {
            return rawName;
        }
        if (!stringNamed && !isMethodNamedNew && IsNumericLiteralName(rawName) && JsNumber.FromString(rawName) >= 0)
        {
            return JsNumber.ToString(JsNumber.FromString(rawName));
        }
        var quote = singleQuote ? '\'' : '"';
        return quote + EscapeStringLiteral(rawName, quote) + quote;
    }

    private bool IsStringNamed(NodeRef d)
    {
        var name = GetNameOfDeclarationRef(d);
        if (name.IsNone)
        {
            return false;
        }
        var store = StoreOf(name);
        return store.Kind(name.Node) == SyntaxKind.StringLiteral
            || (store.Kind(name.Node) == SyntaxKind.ComputedPropertyName && Utilities.IsStringLiteralLike(store, store.Expression(name.Node)));
    }

    private bool IsSingleQuotedStringNamed(NodeRef d)
    {
        var name = GetNameOfDeclarationRef(d);
        if (name.IsNone)
        {
            return false;
        }
        var store = StoreOf(name);
        var n = name.Node;
        if (store.Kind(n) == SyntaxKind.ComputedPropertyName)
        {
            n = store.Expression(n);
        }
        return store.Kind(n) == SyntaxKind.StringLiteral && store.SingleQuote(n);
    }

    // utilities.ts isNumericLiteralName: (+name).toString() === name
    private static bool IsNumericLiteralName(string name) => JsNumber.ToString(JsNumber.FromString(name)) == name;

    private Type RemoveUndefinedForOptional(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            var elements = Types.Elements(Types.Union(type).Types).ToArray();
            if (elements.Length > 0 && (elements[0] == undefinedType || elements[0] == missingType))
            {
                return GetUnionType(elements.AsSpan(1));
            }
        }
        return type;
    }

    private bool IsReadonlySymbol(SymbolRef symbol)
    {
        var declaration = ValueDeclarationOf(symbol);
        return !declaration.IsNone && Utilities.HasSyntacticModifierUncached(StoreOf(declaration), declaration.Node, ModifierFlags.Readonly);
    }

    // typeParameterToDeclaration: the name (reused from the declaration) and the constraint (TODO(C2): defaults)
    private string TypeParameterDeclarationText(Type typeParameter, NodeRef enclosingDeclaration, int depth)
    {
        var symbol = Types.Symbol(typeParameter);
        var name = SymbolName(symbol);
        var declaration = symbol.IsNone ? NodeRef.None : GetDeclarationOfKind(symbol, SyntaxKind.TypeParameter);
        if (declaration.IsNone)
        {
            return name;
        }
        var text = Reused(name);
        var constraintNode = At(declaration, StoreOf(declaration).Constraint(declaration.Node));
        if (!constraintNode.IsNone)
        {
            var constraint = GetConstraintOfTypeParameter(typeParameter);
            if (!constraint.IsNone)
            {
                text += " extends " + (TryReuseAnnotation(constraintNode, constraint, false) ?? TypeToText(constraint, enclosingDeclaration, false, false, depth + 1));
            }
        }
        return text;
    }

    private string SignatureText(Signature signature, NodeRef enclosingDeclaration, int depth, bool arrow, bool construct)
    {
        ref var record = ref Signatures.Record(signature);
        var sb = new StringBuilder();
        if (construct)
        {
            sb.Append("new ");
        }
        if (record.TypeParameters.Count > 0)
        {
            sb.Append('<').Append(string.Join(", ", Array.ConvertAll(Types.Elements(record.TypeParameters).ToArray(), t => TypeParameterDeclarationText(t, enclosingDeclaration, depth)))).Append('>');
        }
        sb.Append('(');
        var parameters = record.Parameters ?? [];
        var thisParameter = record.ThisParameter;
        var first = true;
        if (!thisParameter.IsNone)
        {
            var thisDeclaration = ValueDeclarationOf(thisParameter);
            var thisType = GetTypeOfSymbol(thisParameter);
            var fromSource = !thisDeclaration.IsNone && Kind(thisDeclaration) == SyntaxKind.Parameter;
            sb.Append(fromSource ? Reused("this") : "this").Append(": ")
                .Append((fromSource ? TryReuseAnnotation(At(thisDeclaration, StoreOf(thisDeclaration).Type(thisDeclaration.Node)), thisType, false) : null)
                    ?? TypeToText(thisType, enclosingDeclaration, false, false, depth + 1));
            first = false;
        }
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!first)
            {
                sb.Append(", ");
            }
            first = false;
            var parameter = parameters[i];
            var declaration = ValueDeclarationOf(parameter);
            var isRest = !declaration.IsNone && Kind(declaration) == SyntaxKind.Parameter && !StoreOf(declaration).DotDotDotToken(declaration.Node).IsNone;
            var isOptional = i >= record.MinArgumentCount && !isRest;
            var parameterType = GetTypeOfSymbol(parameter);
            if (isOptional)
            {
                parameterType = RemoveUndefinedForOptional(parameterType);
            }
            var isParameterDeclaration = !declaration.IsNone && Kind(declaration) == SyntaxKind.Parameter;
            var nameFromSource = isParameterDeclaration && Kind(At(declaration, StoreOf(declaration).Name(declaration.Node))) == SyntaxKind.Identifier;
            var parameterName = SymbolName(parameter);
            sb.Append(isRest ? "..." : "").Append(nameFromSource ? Reused(parameterName) : parameterName).Append(isOptional ? "?" : "").Append(": ")
                .Append((isParameterDeclaration ? TryReuseAnnotation(At(declaration, StoreOf(declaration).Type(declaration.Node)), parameterType, false) : null)
                    ?? TypeToText(parameterType, enclosingDeclaration, false, false, depth + 1));
        }
        sb.Append(')');
        var returnType = GetReturnTypeOfSignature(signature);
        var signatureDeclaration = record.Declaration;
        string? returnText = null;
        if (!signatureDeclaration.IsNone && Utilities.IsFunctionLike(StoreOf(signatureDeclaration), signatureDeclaration.Node))
        {
            var annotation = At(signatureDeclaration, StoreOf(signatureDeclaration).Type(signatureDeclaration.Node));
            returnText = annotation.IsNone
                ? TypeFromSingleReturnExpressionText(signatureDeclaration, enclosingDeclaration, depth)
                : TryReuseAnnotation(annotation, returnType, false);
        }
        sb.Append(arrow ? " => " : ": ").Append(returnText ?? TypeToText(returnType, enclosingDeclaration, false, false, depth + 1));
        return sb.ToString();
    }

    // expressionToTypeNode.ts typeFromSingleReturnExpression, for a single returned function or arrow expression
    // (typeFromFunctionLikeExpression); other expressions fall back to the checker's type (TODO(C2): literals, object and
    // array literals, assertions). Null when the syntactic builder does not apply.
    private string? TypeFromSingleReturnExpressionText(NodeRef declaration, NodeRef enclosingDeclaration, int depth)
    {
        var store = StoreOf(declaration);
        var n = declaration.Node;
        if (depth > 6 || store.Kind(n) is not (SyntaxKind.FunctionDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction))
        {
            return null;
        }
        var body = store.Body(n);
        if (body.IsNone || (!store.AsteriskToken(n).IsNone && Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Async)))
        {
            return null;
        }
        var candidate = Node.None;
        if (store.Kind(body) == SyntaxKind.Block)
        {
            var failed = false;
            var count = 0;
            ForEachReturnStatementInBody(store, body, statement =>
            {
                if (store.Parent(statement) != body)
                {
                    failed = true;
                }
                count++;
                candidate = count == 1 ? store.Expression(statement) : Node.None;
            });
            if (failed || count != 1)
            {
                return null;
            }
        }
        else
        {
            candidate = body;
        }
        while (!candidate.IsNone && store.Kind(candidate) == SyntaxKind.ParenthesizedExpression)
        {
            candidate = store.Expression(candidate);
        }
        if (candidate.IsNone || store.Kind(candidate) is not (SyntaxKind.ArrowFunction or SyntaxKind.FunctionExpression) || IsContextuallyTypedForSyntacticBuilder(store, candidate))
        {
            return null;
        }
        // typeFromFunctionLikeExpression: a new function type node; the parameters and type parameters are updated from
        // the existing ones (not synthesized), so only their uninferable types are underlined
        var function = At(declaration, candidate);
        var sb = new StringBuilder();
        var typeParameters = store.TypeParameters(candidate);
        if (!typeParameters.IsNone && store.Length(typeParameters) > 0)
        {
            var parts = new List<string>();
            foreach (var typeParameter in store.Nodes(typeParameters))
            {
                var constraint = store.Constraint(typeParameter);
                if (!store.Default(typeParameter).IsNone || !store.Modifiers(typeParameter).IsNone || (!constraint.IsNone && !IsSimpleReusableTypeNode(store, constraint)))
                {
                    return null;
                }
                parts.Add(Reused(Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, typeParameter), store.Name(typeParameter)) +
                    (constraint.IsNone ? "" : " extends " + PrintReusedTypeNode(store, Utilities.RootOf(store, constraint), constraint))));
            }
            sb.Append('<').Append(string.Join(", ", parts)).Append('>');
        }
        sb.Append('(');
        var firstParameter = true;
        foreach (var parameterNode in store.Nodes(store.Parameters(candidate)))
        {
            if (!firstParameter)
            {
                sb.Append(", ");
            }
            firstParameter = false;
            var parameterRef = At(declaration, parameterNode);
            var nameNode = store.Name(parameterNode);
            if (store.Kind(nameNode) != SyntaxKind.Identifier)
            {
                return null; // (serializeNameOfParameter of binding patterns: C2)
            }
            var parameterSymbol = GetSymbolOfDeclaration(parameterRef);
            if (parameterSymbol.IsNone)
            {
                return null;
            }
            var parameterType = GetTypeOfSymbol(parameterSymbol);
            var isOptional = !store.QuestionToken(parameterNode).IsNone || !store.Initializer(parameterNode).IsNone;
            if (isOptional)
            {
                parameterType = RemoveUndefinedForOptional(parameterType);
            }
            var nameText = (store.DotDotDotToken(parameterNode).IsNone ? "" : "...") + Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, nameNode), nameNode);
            var typeText = TryReuseAnnotation(At(declaration, store.Type(parameterNode)), parameterType, false) ?? TypeToText(parameterType, enclosingDeclaration, false, false, depth + 1);
            sb.Append(Reused(nameText)).Append(isOptional ? "?" : "").Append(Reused(": ")).Append(typeText);
        }
        sb.Append(") => ");
        // createReturnFromSignature for the function expression
        var functionSymbol = GetSymbolOfDeclaration(function);
        var functionSignature = functionSymbol.IsNone ? Signature.None : GetSignatureFromDeclaration(function);
        var functionReturnType = functionSignature.IsNone ? anyType : GetReturnTypeOfSignature(functionSignature);
        var returnAnnotation = At(declaration, store.Type(candidate));
        var returnText = returnAnnotation.IsNone
            ? TypeFromSingleReturnExpressionText(function, enclosingDeclaration, depth + 1)
            : TryReuseAnnotation(returnAnnotation, functionReturnType, false);
        sb.Append(returnText ?? TypeToText(functionReturnType, enclosingDeclaration, false, false, depth + 1));
        return sb.ToString();
    }

    // expressionToTypeNode.ts isContextuallyTyped: a call, a type-annotated non-function ancestor or JSX
    private static bool IsContextuallyTypedForSyntacticBuilder(NodeStore store, Node node)
    {
        for (var n = store.Parent(node); !n.IsNone; n = store.Parent(n))
        {
            var kind = store.Kind(n);
            if (kind is SyntaxKind.CallExpression or SyntaxKind.JsxElement or SyntaxKind.JsxExpression or SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression or SyntaxKind.SatisfiesExpression)
            {
                return true;
            }
            if (!Utilities.IsFunctionLikeDeclaration(store, n) && kind is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.BindingElement
                && !store.Type(n).IsNone)
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts forEachReturnStatement (the body's return statements, not those of nested functions)
    private static void ForEachReturnStatementInBody(NodeStore store, Node body, Action<Node> visitor)
    {
        Traverse(body);

        void Traverse(Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ReturnStatement:
                    visitor(node);
                    return;
                case SyntaxKind.CaseBlock:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.CatchClause:
                    NodeVisitor.ForEachChild<object>(store, node, child =>
                    {
                        Traverse(child);
                        return null;
                    });
                    return;
            }
        }
    }

    // The printer's double-quoted string literal with escapes (escapeString, no ASCII escaping)
    private static string Quote(string value) => "\"" + EscapeStringLiteral(value, '"') + "\"";

    private static string EscapeStringLiteral(string value, char quote)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\v':
                    sb.Append("\\v");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\0':
                    sb.Append("\\0");
                    break;
                case (char)0x2028:
                    sb.Append("\\u2028");
                    break;
                case (char)0x2029:
                    sb.Append("\\u2029");
                    break;
                case '\u0085':
                    sb.Append("\\u0085");
                    break;
                default:
                    if (c == quote)
                    {
                        sb.Append('\\').Append(c);
                    }
                    else if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
