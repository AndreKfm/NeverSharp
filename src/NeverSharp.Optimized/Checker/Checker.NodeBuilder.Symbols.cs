// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createNodeBuilder: signatures, parameters, type parameters, scopes, symbol chains and names, property names, serialize*ForDeclaration/Signature; getNameOfSymbolAsWritten) and src/compiler/utilities.ts (createPropertyNameNodeForIdentifierOrLiteral)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/nodebuilderimpl.go and internal/checker/nodebuilderscopes.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C2. Module specifiers (getSpecifierForModuleSymbol) are those of
// Gate C's programs: ambient module names, and for source file modules the file name relative to the enclosing file
// (moduleSpecifiers.getModuleSpecifiers in full is not reachable in Gate C's one-file programs and is not ported).

using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts signatureToSignatureDeclarationHelper
    private Node SignatureToSignatureDeclarationHelper(Signature signature, SyntaxKind kind, NodeBuilderContext context, Node name = default, Node questionToken = default, NodeList modifiers = default)
    {
        List<Node>? typeParameters = null;
        List<Node>? typeArguments = null;
        ref var record = ref Signatures.Record(signature);
        var expandedParams = GetExpandedParameters(signature, skipUnionExpanding: true)[0];
        var signatureTypeParameters = Types.Elements(record.TypeParameters).ToArray();
        var cleanup = EnterNewScope(context, record.Declaration, expandedParams, signatureTypeParameters, record.Parameters, GetSignatureMapper(signature));
        context.ApproximateLength += 3; // Usually a signature contributes a few more characters than this, but 3 is the minimum

        var target = Signatures.Record(signature).Target;
        if ((context.Flags & NodeBuilderFlags.WriteTypeArgumentsOfSignature) != 0 && !target.IsNone && !GetSignatureMapper(signature).IsNone &&Signatures.Record(target).TypeParameters.Count > 0)
        {
            typeArguments = [];
            foreach (var parameter in Types.Elements(Signatures.Record(target).TypeParameters).ToArray())
            {
                typeArguments.Add(TypeToTypeNodeHelper(InstantiateTypeWithMapper(parameter, GetSignatureMapper(signature)), context));
            }
        }
        else if (record.TypeParameters.Count > 0)
        {
            typeParameters = [];
            foreach (var parameter in signatureTypeParameters)
            {
                typeParameters.Add(TypeParameterToDeclaration(parameter, context));
            }
        }

        var saved = SaveRestoreFlags(context);
        context.Flags &= ~NodeBuilderFlags.SuppressAnyReturnType; // SuppressAnyReturnType should only apply to the signature `return` position
        // If the expanded parameter list had a variadic in a non-trailing position, don't expand it
        var parameterSymbols = expandedParams;
        for (var i = 0; i < expandedParams.Length - 1; i++)
        {
            if ((GetCheckFlags(expandedParams[i]) & CheckFlags.RestParameter) != 0)
            {
                parameterSymbols = Signatures.Record(signature).Parameters ?? [];
                break;
            }
        }
        var parameters = new List<Node>();
        foreach (var parameter in parameterSymbols)
        {
            parameters.Add(SymbolToParameterDeclaration(parameter, context, kind == SyntaxKind.Constructor));
        }
        var thisParameter = (context.Flags & NodeBuilderFlags.OmitThisParameter) != 0 ? Node.None : TryGetThisParameterDeclaration(signature, context);
        if (!thisParameter.IsNone)
        {
            parameters.Insert(0, thisParameter);
        }
        saved.Restore(context);

        var returnTypeNode = SerializeReturnTypeForSignature(context, signature);

        if (kind == SyntaxKind.ConstructorType && (Signatures.Record(signature).Flags & SignatureFlags.Abstract) != 0)
        {
            var existing = new List<Node>();
            foreach (var m in S.Nodes(modifiers))
            {
                existing.Add(m);
            }
            if (!existing.Exists(m => S.Kind(m) == SyntaxKind.AbstractKeyword))
            {
                existing.Add(SToken(SyntaxKind.AbstractKeyword));
            }
            modifiers = SList(existing);
        }

        Node node;
        switch (kind)
        {
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
                node = SSignature(kind, NodeList.None, Node.None, Node.None, SList(typeParameters), SList(parameters), returnTypeNode);
                break;
            case SyntaxKind.MethodSignature:
                node = SSignature(kind, modifiers, !name.IsNone ? name : SIdentifier(""), questionToken, SList(typeParameters), SList(parameters), returnTypeNode);
                break;
            case SyntaxKind.MethodDeclaration:
                node = SSignature(kind, modifiers, !name.IsNone ? name : SIdentifier(""), Node.None, SList(typeParameters), SList(parameters), returnTypeNode);
                break;
            case SyntaxKind.Constructor:
                node = SSignature(kind, modifiers, Node.None, Node.None, NodeList.None, SList(parameters), Node.None);
                break;
            case SyntaxKind.GetAccessor:
                node = SSignature(kind, modifiers, !name.IsNone ? name : SIdentifier(""), Node.None, NodeList.None, SList(parameters), returnTypeNode);
                break;
            case SyntaxKind.SetAccessor:
                node = SSignature(kind, modifiers, !name.IsNone ? name : SIdentifier(""), Node.None, NodeList.None, SList(parameters), Node.None);
                break;
            case SyntaxKind.IndexSignature:
                node = SSignature(kind, modifiers, Node.None, Node.None, NodeList.None, SList(parameters), returnTypeNode);
                break;
            case SyntaxKind.FunctionType:
                node = SSignature(kind, NodeList.None, Node.None, Node.None, SList(typeParameters), SList(parameters), !returnTypeNode.IsNone ? returnTypeNode : STypeReference(SIdentifier(""), NodeList.None));
                break;
            case SyntaxKind.ConstructorType:
                node = SSignature(kind, modifiers, Node.None, Node.None, SList(typeParameters), SList(parameters), !returnTypeNode.IsNone ? returnTypeNode : STypeReference(SIdentifier(""), NodeList.None));
                break;
            case SyntaxKind.FunctionDeclaration:
                // (module D5: symbolTableToDeclarationStatements)
                node = SSignature(kind, modifiers, !name.IsNone ? name : SIdentifier(""), Node.None, SList(typeParameters), SList(parameters), returnTypeNode);
                break;
            default:
                throw new NotSupportedException("signatureToSignatureDeclarationHelper: " + kind);
        }
        if (typeArguments is not null)
        {
            S.SetTypeArguments(node, SList(typeArguments));
        }
        var signatureDeclaration = Signatures.Record(signature).Declaration;
        if (!signatureDeclaration.IsNone && Kind(signatureDeclaration) == SyntaxKind.JSDocSignature && Kind(Parent(signatureDeclaration)) == SyntaxKind.JSDocOverloadTag)
        {
            // the text of the JSDoc comment as a synthetic comment (printed by declaration emit)
            var jsdoc = Parent(Parent(signatureDeclaration));
            var jstore = StoreOf(jsdoc);
            var text = Utilities.GetSourceTextOfNodeFromSourceFile(jstore, Utilities.RootOf(jstore, jsdoc.Node), jsdoc.Node, includeTrivia: true);
            text = text.Length >= 4 ? text[2..^2] : "";
            var lines = System.Text.RegularExpressions.Regex.Split(text, "\r\n|\n|\r");
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = System.Text.RegularExpressions.Regex.Replace(lines[i], "^\\s+", " ");
            }
            SAddSyntheticLeadingComment(node, string.Join("\n", lines), hasTrailingNewLine: true);
        }
        cleanup();
        return node;
    }

    // checker.ts cloneNodeBuilderContext: make type parameters created within this context not consume the name outside this context
    private static Action CloneNodeBuilderContext(NodeBuilderContext context)
    {
        var oldMustCreateTypeParameterSymbolList = context.MustCreateTypeParameterSymbolList;
        var oldMustCreateTypeParametersNamesLookups = context.MustCreateTypeParametersNamesLookups;
        context.MustCreateTypeParameterSymbolList = true;
        context.MustCreateTypeParametersNamesLookups = true;
        var oldTypeParameterNames = context.TypeParameterNames;
        var oldTypeParameterNamesByText = context.TypeParameterNamesByText;
        var oldTypeParameterNamesByTextNextNameCount = context.TypeParameterNamesByTextNextNameCount;
        var oldTypeParameterSymbolList = context.TypeParameterSymbolList;
        return () =>
        {
            context.TypeParameterNames = oldTypeParameterNames;
            context.TypeParameterNamesByText = oldTypeParameterNamesByText;
            context.TypeParameterNamesByTextNextNameCount = oldTypeParameterNamesByTextNextNameCount;
            context.TypeParameterSymbolList = oldTypeParameterSymbolList;
            context.MustCreateTypeParameterSymbolList = oldMustCreateTypeParameterSymbolList;
            context.MustCreateTypeParametersNamesLookups = oldMustCreateTypeParametersNamesLookups;
        };
    }

    // checker.ts enterNewScope
    private Action EnterNewScope(NodeBuilderContext context, NodeRef declaration, SymbolRef[]? expandedParams, Type[]? typeParameters, SymbolRef[]? originalParameters = null, TypeMapper mapper = default)
    {
        var cleanupContext = CloneNodeBuilderContext(context);
        // For regular function/method declarations, the enclosing declaration will already be signature.declaration,
        // so this is a no-op, but for arrow functions and function expressions, the enclosing declaration will be
        // the declaration that the arrow function / function expression is assigned to.
        //
        // If the parameters or return type include "typeof globalThis.paramName", using the wrong scope will lead
        // us to believe that we can emit "typeof paramName" instead, even though that would refer to the parameter,
        // not the global. Make sure we are in the right scope by changing the enclosingDeclaration to the function.
        //
        // We can't use the declaration directly; it may be in another file and so we may lose access to symbols
        // accessible to the current enclosing declaration, or gain access to symbols not accessible to the current
        // enclosing declaration. To keep this chain accurate, insert a fake scope into the chain which makes the
        // function's parameters visible.
        Action? cleanupParams = null;
        Action? cleanupTypeParams = null;
        var oldEnclosingDecl = context.EnclosingDeclaration;
        var oldFakeScope = context.FakeScope;
        var oldMapper = context.Mapper;
        if (!mapper.IsNone)
        {
            context.Mapper = mapper;
        }
        if ((!context.EnclosingDeclaration.IsNone || context.FakeScope is not null) && !declaration.IsNone)
        {
            cleanupParams = expandedParams is not { Length: > 0 } ? null : PushFakeScope("params", add =>
            {
                for (var pIndex = 0; pIndex < expandedParams.Length; pIndex++)
                {
                    var param = expandedParams[pIndex];
                    var originalParam = originalParameters is not null && pIndex < originalParameters.Length ? originalParameters[pIndex] : SymbolRef.None;
                    if (originalParameters is not null && originalParam != param)
                    {
                        // Can't reference parameters that come from an expansion
                        add(NameOf(param), unknownSymbol);
                        // Can't reference the original expanded parameter either
                        if (!originalParam.IsNone)
                        {
                            add(NameOf(originalParam), unknownSymbol);
                        }
                    }
                    else
                    {
                        var boundPattern = false;
                        foreach (var d in DeclarationsOf(param) ?? [])
                        {
                            var store = StoreOf(d);
                            if (store.Kind(d.Node) == SyntaxKind.Parameter && store.Kind(store.Name(d.Node)) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
                            {
                                BindPattern(At(d, store.Name(d.Node)));
                                boundPattern = true;
                                break;
                            }
                        }
                        if (!boundPattern)
                        {
                            add(NameOf(param), param);
                        }
                    }
                }

                void BindPattern(NodeRef pattern)
                {
                    var store = StoreOf(pattern);
                    foreach (var e in store.Nodes(store.Elements(pattern.Node)))
                    {
                        if (store.Kind(e) == SyntaxKind.OmittedExpression)
                        {
                            continue;
                        }
                        var elementName = store.Name(e);
                        if (store.Kind(elementName) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
                        {
                            BindPattern(At(pattern, elementName));
                            continue;
                        }
                        var symbol = GetSymbolOfDeclaration(At(pattern, e));
                        add(NameOf(symbol), symbol);
                    }
                }
            });

            if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && typeParameters is { Length: > 0 })
            {
                cleanupTypeParams = PushFakeScope("typeParams", add =>
                {
                    foreach (var typeParam in typeParameters)
                    {
                        var typeParamName = S.Texts.GetString(S.EscapedText(TypeParameterToName(typeParam, context))) ?? "";
                        add(typeParamName, Types.Symbol(typeParam));
                    }
                });
            }
        }

        return () =>
        {
            cleanupParams?.Invoke();
            cleanupTypeParams?.Invoke();
            cleanupContext();
            context.EnclosingDeclaration = oldEnclosingDecl;
            context.FakeScope = oldFakeScope;
            context.Mapper = oldMapper;
        };

        Action? PushFakeScope(string kind, Action<Action<string, SymbolRef>> addAll)
        {
            // We only ever need to look two declarations upward.
            FakeScope? existingFakeScope = null;
            if (context.FakeScope is not null && context.FakeScope.Kind == kind)
            {
                existingFakeScope = context.FakeScope;
            }
            else if (context.FakeScope?.Parent is not null && context.FakeScope.Parent.Kind == kind)
            {
                existingFakeScope = context.FakeScope.Parent;
            }
            var locals = existingFakeScope?.Locals ?? new SymTable(Names);
            List<string>? newLocals = null;
            List<(string Name, SymbolRef OldSymbol)>? oldLocals = null;
            addAll((name, symbol) =>
            {
                if (existingFakeScope is not null)
                {
                    var oldSymbol = locals.Get(name);
                    if (oldSymbol.IsNone)
                    {
                        (newLocals ??= []).Add(name);
                    }
                    else
                    {
                        (oldLocals ??= []).Add((name, oldSymbol));
                    }
                }
                locals.Set(name, symbol);
            });
            if (existingFakeScope is null)
            {
                // Use a Block for this; the type of the node doesn't matter so long as it
                // has locals, and this is cheaper/easier than using a function-ish Node.
                var fakeScope = new FakeScope { Kind = kind, Parent = context.FakeScope, Locals = new SymTable(Names) };
                for (var i = 0; i < locals.Count; i++)
                {
                    fakeScope.Locals.Set(locals.NameIdAt(i), locals.ValueAt(i));
                }
                context.FakeScope = fakeScope;
                return null;
            }
            // We did not create the current scope, so we have to clean it up
            // (as in TypeScript, whose undo runs `forEach(newLocals, s => locals.delete(s))` and
            // `forEach(oldLocals, s => locals.set(s.name, s.oldSymbol))`: core.ts forEach stops at the first truthy
            // result, so only the first name deleted and the first name restored are undone; the others stay visible
            // to typeParameterShadowsOtherTypeParameterInScope of sibling signatures)
            return () =>
            {
                foreach (var s in newLocals ?? [])
                {
                    if (locals.Delete(s))
                    {
                        break;
                    }
                }
                if (oldLocals is { Count: > 0 })
                {
                    locals.Set(oldLocals[0].Name, oldLocals[0].OldSymbol);
                }
            };
        }
    }

    // checker.ts tryGetThisParameterDeclaration
    private Node TryGetThisParameterDeclaration(Signature signature, NodeBuilderContext context)
    {
        var thisParameter = Signatures.Record(signature).ThisParameter;
        if (!thisParameter.IsNone)
        {
            return SymbolToParameterDeclaration(thisParameter, context);
        }
        // (@this tags of JavaScript declarations: C9)
        return Node.None;
    }

    // checker.ts typeParameterToDeclarationWithConstraint
    private Node TypeParameterToDeclarationWithConstraint(Type type, NodeBuilderContext context, Node constraintNode)
    {
        var saved = SaveRestoreFlags(context);
        context.Flags &= ~NodeBuilderFlags.WriteTypeParametersInQualifiedName; // Avoids potential infinite loop when building for a claimspace with a generic
        var modifiers = CreateModifiersFromModifierFlags(GetTypeParameterModifiers(type));
        var name = TypeParameterToName(type, context);
        var defaultParameter = GetDefaultFromTypeParameter(type);
        var defaultParameterNode = defaultParameter.IsNone ? Node.None : TypeToTypeNodeHelper(defaultParameter, context);
        saved.Restore(context);
        return STypeParameterDeclaration(modifiers, name, constraintNode, defaultParameterNode);
    }

    // nodeFactory.ts createModifiersFromModifierFlags
    private NodeList CreateModifiersFromModifierFlags(ModifierFlags flags)
    {
        if (flags == 0)
        {
            return NodeList.None;
        }
        var result = new List<Node>();
        if ((flags & ModifierFlags.Export) != 0) result.Add(SToken(SyntaxKind.ExportKeyword));
        if ((flags & ModifierFlags.Ambient) != 0) result.Add(SToken(SyntaxKind.DeclareKeyword));
        if ((flags & ModifierFlags.Default) != 0) result.Add(SToken(SyntaxKind.DefaultKeyword));
        if ((flags & ModifierFlags.Const) != 0) result.Add(SToken(SyntaxKind.ConstKeyword));
        if ((flags & ModifierFlags.Public) != 0) result.Add(SToken(SyntaxKind.PublicKeyword));
        if ((flags & ModifierFlags.Private) != 0) result.Add(SToken(SyntaxKind.PrivateKeyword));
        if ((flags & ModifierFlags.Protected) != 0) result.Add(SToken(SyntaxKind.ProtectedKeyword));
        if ((flags & ModifierFlags.Abstract) != 0) result.Add(SToken(SyntaxKind.AbstractKeyword));
        if ((flags & ModifierFlags.Static) != 0) result.Add(SToken(SyntaxKind.StaticKeyword));
        if ((flags & ModifierFlags.Override) != 0) result.Add(SToken(SyntaxKind.OverrideKeyword));
        if ((flags & ModifierFlags.Readonly) != 0) result.Add(SToken(SyntaxKind.ReadonlyKeyword));
        if ((flags & ModifierFlags.Accessor) != 0) result.Add(SToken(SyntaxKind.AccessorKeyword));
        if ((flags & ModifierFlags.Async) != 0) result.Add(SToken(SyntaxKind.AsyncKeyword));
        if ((flags & ModifierFlags.In) != 0) result.Add(SToken(SyntaxKind.InKeyword));
        if ((flags & ModifierFlags.Out) != 0) result.Add(SToken(SyntaxKind.OutKeyword));
        return result.Count > 0 ? SList(result) : NodeList.None;
    }

    // checker.ts typeToTypeNodeHelperWithPossibleReusableTypeNode
    private Node TypeToTypeNodeHelperWithPossibleReusableTypeNode(Type type, NodeRef typeNode, NodeBuilderContext context)
    {
        if (!CanPossiblyExpandType(type, context) && !typeNode.IsNone && GetTypeFromTypeNodeInContext(context, typeNode) == type)
        {
            var reused = TryReuseExistingTypeNodeChecked(context, typeNode);
            if (!reused.IsNone)
            {
                return reused;
            }
        }
        return TypeToTypeNodeHelper(type, context);
    }

    // checker.ts typeParameterToDeclaration
    private Node TypeParameterToDeclaration(Type type, NodeBuilderContext context)
    {
        var constraint = GetConstraintOfTypeParameter(type);
        var constraintNode = constraint.IsNone ? Node.None : TypeToTypeNodeHelperWithPossibleReusableTypeNode(constraint, GetConstraintDeclaration(type), context);
        return TypeParameterToDeclarationWithConstraint(type, context, constraintNode);
    }

    // checker.ts typePredicateToTypePredicateNodeHelper
    private Node TypePredicateToTypePredicateNodeHelper(TypePredicateInfo typePredicate, NodeBuilderContext context)
    {
        var assertsModifier = typePredicate.Kind is TypePredicateKind.AssertsThis or TypePredicateKind.AssertsIdentifier ? SToken(SyntaxKind.AssertsKeyword) : Node.None;
        var parameterName = typePredicate.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier
            ? SetEmitFlags(SIdentifier(typePredicate.ParameterName!), EmitFlags.NoAsciiEscaping)
            : SToken(SyntaxKind.ThisType);
        var typeNode = typePredicate.Type.IsNone ? Node.None : TypeToTypeNodeHelper(typePredicate.Type, context);
        var node = SNew(SyntaxKind.TypePredicate);
        S.SetAssertsModifier(node, assertsModifier);
        S.SetParameterName(node, parameterName);
        S.SetType(node, typeNode);
        return node;
    }

    // checker.ts getEffectiveParameterDeclaration
    private NodeRef GetEffectiveParameterDeclaration(SymbolRef parameterSymbol)
    {
        var parameterDeclaration = GetDeclarationOfKind(parameterSymbol, SyntaxKind.Parameter);
        if (!parameterDeclaration.IsNone)
        {
            return parameterDeclaration;
        }
        if (!parameterSymbol.IsTransient)
        {
            return GetDeclarationOfKind(parameterSymbol, SyntaxKind.JSDocParameterTag);
        }
        return NodeRef.None;
    }

    // checker.ts symbolToParameterDeclaration
    private Node SymbolToParameterDeclaration(SymbolRef parameterSymbol, NodeBuilderContext context, bool preserveModifierFlags = false)
    {
        var parameterDeclaration = GetEffectiveParameterDeclaration(parameterSymbol);
        var parameterType = GetTypeOfSymbol(parameterSymbol);
        var parameterTypeNode = SerializeTypeForDeclaration(context, parameterDeclaration, parameterType, parameterSymbol);
        var modifiers = NodeList.None;
        if ((context.Flags & NodeBuilderFlags.OmitParameterModifiers) == 0 && preserveModifierFlags && !parameterDeclaration.IsNone && Kind(parameterDeclaration) == SyntaxKind.Parameter)
        {
            var store = StoreOf(parameterDeclaration);
            var copied = new List<Node>();
            foreach (var modifier in store.Nodes(store.Modifiers(parameterDeclaration.Node)))
            {
                if (store.Kind(modifier) != SyntaxKind.Decorator)
                {
                    copied.Add(CopyShell(At(parameterDeclaration, modifier)));
                }
            }
            modifiers = copied.Count > 0 ? SList(copied) : NodeList.None;
        }
        var isRest = (!parameterDeclaration.IsNone && IsRestParameterOrTag(StoreOf(parameterDeclaration), parameterDeclaration.Node)) ||
            (GetCheckFlags(parameterSymbol) & CheckFlags.RestParameter) != 0;
        var dotDotDotToken = isRest ? SToken(SyntaxKind.DotDotDotToken) : Node.None;
        var name = ParameterToParameterDeclarationName(parameterSymbol, parameterDeclaration, context);
        var isOptional = (!parameterDeclaration.IsNone && IsOptionalParameter(parameterDeclaration)) || (GetCheckFlags(parameterSymbol) & CheckFlags.OptionalParameter) != 0;
        var questionToken = isOptional ? SToken(SyntaxKind.QuestionToken) : Node.None;
        var parameterNode = SParameterDeclaration(modifiers, dotDotDotToken, name, questionToken, parameterTypeNode);
        context.ApproximateLength += SymbolName(parameterSymbol).Length + 3;
        return parameterNode;
    }

    // checker.ts parameterToParameterDeclarationName
    private Node ParameterToParameterDeclarationName(SymbolRef parameterSymbol, NodeRef parameterDeclaration, NodeBuilderContext context)
    {
        if (parameterDeclaration.IsNone)
        {
            return SIdentifier(SymbolName(parameterSymbol));
        }
        var store = StoreOf(parameterDeclaration);
        var name = store.Name(parameterDeclaration.Node);
        if (name.IsNone)
        {
            return SIdentifier(SymbolName(parameterSymbol));
        }
        // (factory.cloneNode: a clone has no positions, so declaration emit prints no comments of the name)
        var oldNoCopyPositions = noCopyPositions;
        noCopyPositions = true;
        try
        {
            switch (store.Kind(name))
            {
                case SyntaxKind.Identifier:
                    // factory.cloneNode of the existing name (its original is the name: reused)
                    return SetEmitFlags(CopyShell(At(parameterDeclaration, name)), EmitFlags.NoAsciiEscaping);
                case SyntaxKind.QualifiedName:
                    return SetEmitFlags(CopyShell(At(parameterDeclaration, store.Right(name))), EmitFlags.NoAsciiEscaping);
                default:
                    return CloneBindingName(At(parameterDeclaration, name), context);
            }
        }
        finally
        {
            noCopyPositions = oldNoCopyPositions;
        }
    }

    // Module D5: copies made for factory.cloneNode keep no positions in declaration emit (see CopyShellFrom)
    private bool noCopyPositions;

    // checker.ts lookupSymbolChain (typeToString's tracker has no inner tracker: trackSymbol does nothing)
    private List<SymbolRef> LookupSymbolChain(SymbolRef symbol, NodeBuilderContext context, SymbolFlags meaning, bool yieldModuleSymbol = false)
    {
        context.Tracker.TrackSymbol(symbol, context.EnclosingDeclaration, context.FakeScope, meaning);
        return LookupSymbolChainWorker(symbol, context, meaning, yieldModuleSymbol);
    }

    // checker.ts lookupSymbolChainWorker
    private List<SymbolRef> LookupSymbolChainWorker(SymbolRef symbol, NodeBuilderContext context, SymbolFlags meaning, bool yieldModuleSymbol)
    {
        // Try to get qualified name if the symbol is not a type parameter and there is an enclosing declaration.
        var isTypeParameter = (SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) != 0;
        if (!isTypeParameter && (!context.EnclosingDeclaration.IsNone || context.FakeScope is not null || (context.Flags & NodeBuilderFlags.UseFullyQualifiedType) != 0) && (context.InternalFlags & InternalNodeBuilderFlags.DoNotIncludeSymbolChain) == 0)
        {
            var chain = GetSymbolChain(symbol, meaning, endOfChain: true, 0);
            return chain is { Count: > 0 } ? chain : [symbol];
        }
        return [symbol];

        // endOfChain: set to false for recursive calls; non-recursive calls should always output something.
        List<SymbolRef>? GetSymbolChain(SymbolRef symbol, SymbolFlags meaning, bool endOfChain, int depth)
        {
            if (depth > 64)
            {
                return null;
            }
            var accessibleSymbolChain = GetAccessibleSymbolChain(symbol, context.EnclosingDeclaration, context.FakeScope, meaning, (context.Flags & NodeBuilderFlags.UseOnlyExternalAliasing) != 0);
            if (accessibleSymbolChain is null ||
                NeedsQualification(accessibleSymbolChain[0], context.EnclosingDeclaration, context.FakeScope, accessibleSymbolChain.Count == 1 ? meaning : GetQualifiedLeftMeaning(meaning)))
            {
                // Go up and add our parent.
                var parents = GetContainersOfSymbol(accessibleSymbolChain is not null ? accessibleSymbolChain[0] : symbol, context.EnclosingDeclaration, context.FakeScope, meaning);
                if (parents is { Count: > 0 })
                {
                    var parentSpecifiers = parents.ConvertAll(p => (DeclarationsOf(p) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol) ? GetSpecifierForModuleSymbol(p, context) : null);
                    // indices sorted by the best name (Array.prototype.sort is stable: an insertion sort)
                    var sorted = new List<int>(parents.Count);
                    for (var i = 0; i < parents.Count; i++)
                    {
                        var at = sorted.Count;
                        while (at > 0 && SortByBestName(parentSpecifiers, sorted[at - 1], i) > 0)
                        {
                            at--;
                        }
                        sorted.Insert(at, i);
                    }
                    foreach (var parentIndex in sorted)
                    {
                        var parent = parents[parentIndex];
                        var parentChain = GetSymbolChain(parent, GetQualifiedLeftMeaning(meaning), endOfChain: false, depth + 1);
                        if (parentChain is not null)
                        {
                            var exportEquals = ExportsOf(parent)?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
                            if (!exportEquals.IsNone && !GetSymbolIfSameReference(exportEquals, symbol).IsNone)
                            {
                                // parentChain root _is_ symbol - symbol is a module export=, so it kinda looks like it's own parent
                                // No need to lookup an alias for the symbol in itself
                                accessibleSymbolChain = parentChain;
                                break;
                            }
                            if (accessibleSymbolChain is not null)
                            {
                                accessibleSymbolChain = [.. parentChain, .. accessibleSymbolChain];
                            }
                            else
                            {
                                var alias = GetAliasForSymbolInContainer(parent, symbol);
                                accessibleSymbolChain = [.. parentChain, !alias.IsNone ? alias : symbol];
                            }
                            break;
                        }
                    }
                }
            }
            if (accessibleSymbolChain is not null)
            {
                return accessibleSymbolChain;
            }
            if (
                // If this is the last part of outputting the symbol, always output. The cases apply only to parent symbols.
                endOfChain ||
                // If a parent symbol is an anonymous type, don't write it.
                (SymbolFlagsOf(symbol) & (SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) == 0)
            {
                // If a parent symbol is an external module, don't write it. (We prefer just `x` vs `"foo/bar".x`.)
                if (!endOfChain && !yieldModuleSymbol && (DeclarationsOf(symbol) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
                {
                    return null;
                }
                return [symbol];
            }
            return null;
        }
    }

    private static int SortByBestName(List<string?> parentSpecifiers, int a, int b)
    {
        var specifierA = parentSpecifiers[a];
        var specifierB = parentSpecifiers[b];
        if (specifierA is not null && specifierB is not null)
        {
            var isBRelative = PathIsRelative(specifierB);
            if (PathIsRelative(specifierA) == isBRelative)
            {
                // Both relative or both non-relative, sort by number of parts
                return CountPathComponents(specifierA) - CountPathComponents(specifierB);
            }
            if (isBRelative)
            {
                // A is non-relative, B is relative: prefer A
                return -1;
            }
            // A is relative, B is non-relative: prefer B
            return 1;
        }
        return 0;
    }

    // path.ts pathIsRelative: /^\.\.?($|[\\/])/
    private static bool PathIsRelative(string path) =>
        path.Length > 0 && path[0] == '.' &&
        (path.Length == 1 || path[1] is '/' or '\\' || (path[1] == '.' && (path.Length == 2 || path[2] is '/' or '\\')));

    // moduleSpecifiers.ts countPathComponents
    private static int CountPathComponents(string path)
    {
        var count = 0;
        for (var i = path.StartsWith("./", StringComparison.Ordinal) ? 2 : 0; i < path.Length; i++)
        {
            if (path[i] == '/')
            {
                count++;
            }
        }
        return count;
    }

    // checker.ts typeParametersToTypeParameterDeclarations
    private List<Node>? TypeParametersToTypeParameterDeclarations(SymbolRef symbol, NodeBuilderContext context)
    {
        var targetSymbol = GetTargetSymbol(symbol);
        if ((SymbolFlagsOf(targetSymbol) & (SymbolFlags.Class | SymbolFlags.Interface | SymbolFlags.TypeAlias)) != 0)
        {
            var result = new List<Node>();
            foreach (var tp in GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol) ?? [])
            {
                result.Add(TypeParameterToDeclaration(tp, context));
            }
            return result;
        }
        return null;
    }

    // checker.ts lookupTypeParameterNodes
    private List<Node>? LookupTypeParameterNodes(List<SymbolRef> chain, int index, NodeBuilderContext context)
    {
        var symbol = chain[index];
        if (context.TypeParameterSymbolList is not null && context.TypeParameterSymbolList.Contains(symbol))
        {
            return null;
        }
        if (context.MustCreateTypeParameterSymbolList)
        {
            context.MustCreateTypeParameterSymbolList = false;
            context.TypeParameterSymbolList = context.TypeParameterSymbolList is null ? [] : [.. context.TypeParameterSymbolList];
        }
        context.TypeParameterSymbolList!.Add(symbol);
        List<Node>? typeParameterNodes = null;
        if ((context.Flags & NodeBuilderFlags.WriteTypeParametersInQualifiedName) != 0 && index < chain.Count - 1)
        {
            var parentSymbol = symbol;
            var nextSymbol = chain[index + 1];
            if ((GetCheckFlags(nextSymbol) & CheckFlags.Instantiated) != 0)
            {
                var parameters = GetTypeParametersOfClassOrInterface((SymbolFlagsOf(parentSymbol) & SymbolFlags.Alias) != 0 ? ResolveAlias(parentSymbol) : parentSymbol);
                typeParameterNodes = MapToTypeNodes(parameters.ConvertAll(t => GetMappedTypeOfInstantiatedSymbol(t, nextSymbol)).ToArray(), context);
            }
            else
            {
                typeParameterNodes = TypeParametersToTypeParameterDeclarations(symbol, context);
            }
        }
        return typeParameterNodes;
    }

    // checker.ts getSpecifierForModuleSymbol (Gate C: ambient module names; source file modules by their file name)
    private string GetSpecifierForModuleSymbol(SymbolRef symbol, NodeBuilderContext context)
    {
        var file = GetDeclarationOfKind(symbol, SyntaxKind.SourceFile);
        if (file.IsNone)
        {
            foreach (var d in DeclarationsOf(symbol) ?? [])
            {
                var equivalentFileSymbol = GetFileSymbolIfFileSymbolExportEqualsContainer(d, symbol);
                if (!equivalentFileSymbol.IsNone)
                {
                    file = GetDeclarationOfKind(equivalentFileSymbol, SyntaxKind.SourceFile);
                    break;
                }
            }
        }
        var escapedName = NameOf(symbol);
        if (file.IsNone && IsAmbientModuleSymbolName(escapedName))
        {
            return escapedName[1..^1];
        }
        if (context.EnclosingFile == 0)
        {
            // If there's no context declaration, we can't lookup a non-ambient specifier, so we just use the symbol name
            if (IsAmbientModuleSymbolName(escapedName))
            {
                return escapedName[1..^1];
            }
            // A resolver may not be provided for baselines and errors - in those cases we use the fileName in full
            // (getSourceFileOfNode(getNonAugmentationDeclaration(symbol)).fileName)
            foreach (var d in DeclarationsOf(symbol) ?? [])
            {
                var store = StoreOf(d);
                if (!IsExternalModuleAugmentation(store, d.Node) && !(store.Kind(d.Node) == SyntaxKind.ModuleDeclaration && Utilities.IsGlobalScopeAugmentation(store, d.Node)))
                {
                    return FileNameOfFile(d.File);
                }
            }
            return !file.IsNone ? FileNameOfFile(file.File) : escapedName;
        }
        if (file.IsNone)
        {
            return escapedName;
        }
        // moduleSpecifiers.getModuleSpecifiers for a file of the program (the specifier preferences in full: not reachable in Gate C's one-file programs)
        var fileName = FileNameOfFile(file.File);
        var withoutExtension = RemoveFileExtensionForSpecifier(fileName);
        // tryGetModuleNameAsNodeModule: the path below the top-level (first) node_modules folder, without a trailing /index
        // (no package.json files exist in Gate C's programs)
        var nodeModules = withoutExtension.IndexOf("/node_modules/", StringComparison.Ordinal);
        if (nodeModules >= 0)
        {
            var moduleName = withoutExtension[(nodeModules + "/node_modules/".Length)..];
            if (moduleName.EndsWith("/index", StringComparison.Ordinal))
            {
                moduleName = moduleName[..^"/index".Length];
            }
            if (moduleName.StartsWith("@types/", StringComparison.Ordinal))
            {
                // getPackageNameFromTypesPackageName
                var unscoped = moduleName["@types/".Length..];
                var mangledScope = unscoped.IndexOf("__", StringComparison.Ordinal);
                moduleName = mangledScope >= 0 ? "@" + unscoped[..mangledScope] + "/" + unscoped[(mangledScope + 2)..] : unscoped;
            }
            return moduleName;
        }
        // getLocalModuleSpecifier: the path relative to the enclosing file's folder (ensurePathIsNonModuleName), its
        // ending by processEnding (no baseUrl, paths, rootDirs; the package.json imports of the project-relative
        // preference are not ported: TODO(C11) if a repository needs them). The ending preference: "js" when the
        // resolution mode is ESNext (getSpecifierForModuleSymbol's importModuleSpecifierEnding: the mode of the enclosing
        // declaration's module specifier, else the file's default mode), else getModuleSpecifierEndingPreference without
        // allowImportingTsExtensions: the .js extension when the importing file's imports use extensions
        // (usesExtensionsOnImports), else the minimal ending (`./index` becomes `.`)
        var contextFile = FileAt(context.EnclosingFile);
        var originalModuleSpecifier = CanHaveModuleSpecifier(context.EnclosingDeclaration) ? TryGetModuleSpecifierFromDeclaration(context.EnclosingDeclaration) : NodeRef.None;
        var resolutionMode = !originalModuleSpecifier.IsNone ? program.GetModeForUsageLocation(contextFile, originalModuleSpecifier.Node) : null;
        if (resolutionMode is null or ModuleKind.None)
        {
            resolutionMode = program.GetDefaultResolutionModeForFile(contextFile);
        }
        var currentDirectory = program.Host.GetCurrentDirectory();
        var sourceDirectory = ModuleNameResolver.GetDirectoryPath(TsPath.GetNormalizedAbsolutePath(FileNameOfFile(context.EnclosingFile), currentDirectory));
        var relativePath = GetRelativePathFromDirectory(sourceDirectory, TsPath.GetNormalizedAbsolutePath(withoutExtension, currentDirectory));
        if (!relativePath.StartsWith('/') && TsPath.GetRootLength(relativePath) == 0 && !ModuleNameResolver.PathIsRelative(relativePath))
        {
            relativePath = "./" + relativePath; // ensurePathIsNonModuleName
        }
        var jsExtension = GetJSExtensionForSpecifier(fileName);
        if (jsExtension is null)
        {
            return relativePath; // .mjs/.cjs kept, .mts/.cts as .mjs/.cjs: processEnding before the preference applies
        }
        if (resolutionMode == ModuleKind.ESNext || UsesExtensionsOnImports(context.EnclosingFile))
        {
            return relativePath + jsExtension;
        }
        return relativePath.EndsWith("/index", StringComparison.Ordinal) ? relativePath[..^"/index".Length] : relativePath;
    }

    // path.ts getRelativePathFromDirectory over normalized absolute paths (getPathComponentsRelativeTo: the roots are
    // compared ignoring case, the other components by the host's canonical file names)
    private string GetRelativePathFromDirectory(string fromDirectory, string to)
    {
        var fromComponents = PathComponentsOf(fromDirectory);
        var toComponents = PathComponentsOf(to);
        var ignoreCase = !program.Host.UseCaseSensitiveFileNames;
        int start;
        for (start = 0; start < fromComponents.Count && start < toComponents.Count; start++)
        {
            var comparison = start == 0 || ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(fromComponents[start], toComponents[start], comparison))
            {
                break;
            }
        }
        if (start == 0)
        {
            return to;
        }
        var components = new List<string>();
        for (var i = start; i < fromComponents.Count; i++)
        {
            components.Add("..");
        }
        for (var i = start; i < toComponents.Count; i++)
        {
            components.Add(toComponents[i]);
        }
        return string.Join('/', components);

        static List<string> PathComponentsOf(string path)
        {
            var rootLength = TsPath.GetRootLength(path);
            var components = new List<string> { path[..rootLength] };
            components.AddRange(path[rootLength..].Split('/', StringSplitOptions.RemoveEmptyEntries));
            return components;
        }
    }

    // utilities.ts canHaveModuleSpecifier
    private bool CanHaveModuleSpecifier(NodeRef node) =>
        !node.IsNone && Kind(node) is SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement or SyntaxKind.ImportDeclaration or SyntaxKind.ExportDeclaration or
            SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ImportClause or SyntaxKind.NamespaceExport or SyntaxKind.NamespaceImport or SyntaxKind.ExportSpecifier or
            SyntaxKind.ImportSpecifier or SyntaxKind.ImportType;

    // utilities.ts tryGetModuleSpecifierFromDeclaration
    private NodeRef TryGetModuleSpecifierFromDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        NodeRef StringLiteralLike(Node n) => !n.IsNone && Utilities.IsStringLiteralLike(store, n) ? At(node, n) : NodeRef.None;
        switch (Kind(node))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.BindingElement:
                for (var n = store.Initializer(node.Node); !n.IsNone; n = store.Parent(n))
                {
                    if (IsRequireCall(At(node, n), requireStringLiteralLikeArgument: true))
                    {
                        foreach (var requireArgument in store.Nodes(store.Arguments(n)))
                        {
                            return At(node, requireArgument);
                        }
                    }
                }
                return NodeRef.None;
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
                return StringLiteralLike(store.ModuleSpecifier(node.Node));
            case SyntaxKind.ImportEqualsDeclaration:
                var moduleReference = store.ModuleReference(node.Node);
                return store.Kind(moduleReference) == SyntaxKind.ExternalModuleReference ? StringLiteralLike(store.Expression(moduleReference)) : NodeRef.None;
            case SyntaxKind.ImportClause:
            case SyntaxKind.NamespaceExport:
                return StringLiteralLike(store.ModuleSpecifier(store.Parent(node.Node)));
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ExportSpecifier:
                return StringLiteralLike(store.ModuleSpecifier(store.Parent(store.Parent(node.Node))));
            case SyntaxKind.ImportSpecifier:
                return StringLiteralLike(store.ModuleSpecifier(store.Parent(store.Parent(store.Parent(node.Node)))));
            default: // ImportType: isLiteralImportTypeNode
                var argument = store.Argument(node.Node);
                return store.Kind(argument) == SyntaxKind.LiteralType ? StringLiteralLike(store.Literal(argument)) : NodeRef.None;
        }
    }

    // moduleSpecifiers.ts getJSExtensionForFile (the .ts/.tsx/.d.ts files of Gate C; .tsx keeps .jsx only under jsx: preserve)
    private string? GetJSExtensionForSpecifier(string fileName)
    {
        if (fileName.EndsWith(".mjs", StringComparison.Ordinal) || fileName.EndsWith(".cjs", StringComparison.Ordinal) ||
            fileName.EndsWith(".mts", StringComparison.Ordinal) || fileName.EndsWith(".cts", StringComparison.Ordinal))
        {
            return null; // handled by RemoveFileExtensionForSpecifier
        }
        if (fileName.EndsWith(".tsx", StringComparison.Ordinal))
        {
            return compilerOptions.Jsx == JsxEmit.Preserve ? ".jsx" : ".js";
        }
        return fileName.EndsWith(".ts", StringComparison.Ordinal) || fileName.EndsWith(".js", StringComparison.Ordinal) ? ".js" : null;
    }

    // utilities.ts usesExtensionsOnImports over the file's imports (program.ts collectExternalModuleReferences: the
    // module specifiers of the top-level import and export declarations and external import-equals references, in order)
    private bool UsesExtensionsOnImports(int fileIndex)
    {
        if (fileIndex == 0)
        {
            return false;
        }
        var file = FileAt(fileIndex);
        var store = file.Store;
        foreach (var statement in store.Nodes(store.Statements(file.Root)))
        {
            var specifier = store.Kind(statement) switch
            {
                SyntaxKind.ImportDeclaration or SyntaxKind.ExportDeclaration => store.ModuleSpecifier(statement),
                SyntaxKind.ImportEqualsDeclaration when store.Kind(store.ModuleReference(statement)) == SyntaxKind.ExternalModuleReference => store.Expression(store.ModuleReference(statement)),
                _ => Node.None,
            };
            if (specifier.IsNone || !Utilities.IsStringLiteralLike(store, specifier))
            {
                continue;
            }
            var text = store.Texts.GetString(store.Text(specifier)) ?? "";
            if (!(text.StartsWith("./", StringComparison.Ordinal) || text.StartsWith("../", StringComparison.Ordinal) || text == "." || text == ".."))
            {
                continue; // pathIsRelative
            }
            if (text.EndsWith(".mjs", StringComparison.Ordinal) || text.EndsWith(".cjs", StringComparison.Ordinal) ||
                text.EndsWith(".mts", StringComparison.Ordinal) || text.EndsWith(".cts", StringComparison.Ordinal) || text.EndsWith(".d.mts", StringComparison.Ordinal) || text.EndsWith(".d.cts", StringComparison.Ordinal))
            {
                continue; // extensionsNotSupportingExtensionlessResolution
            }
            foreach (var extension in (ReadOnlySpan<string>)[".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts"])
            {
                if (text.EndsWith(extension, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
        return false;
    }

    private string FileNameOfFile(int file)
    {
        var sourceFile = FileAt(file);
        return sourceFile.Store.FileName(sourceFile.Root);
    }

    // checker.ts ambientModuleSymbolRegex: /^".+"$/
    private static bool IsAmbientModuleSymbolName(string name) => name.Length >= 3 && name[0] == '"' && name[^1] == '"';

    private static string RemoveFileExtensionForSpecifier(string fileName)
    {
        // moduleSpecifiers.ts removeExtensionAndIndexPostFix / processEnding: .json, .mjs and .cjs are kept, .mts/.cts (and
        // their declaration files) become .mjs/.cjs
        if (fileName.EndsWith(".json", StringComparison.Ordinal) || fileName.EndsWith(".mjs", StringComparison.Ordinal) || fileName.EndsWith(".cjs", StringComparison.Ordinal))
        {
            return fileName;
        }
        foreach (var (extension, jsExtension) in (ReadOnlySpan<(string, string)>)[(".d.mts", ".mjs"), (".d.cts", ".cjs"), (".mts", ".mjs"), (".cts", ".cjs")])
        {
            if (fileName.EndsWith(extension, StringComparison.Ordinal))
            {
                return fileName[..^extension.Length] + jsExtension;
            }
        }
        foreach (var extension in (ReadOnlySpan<string>)[".d.ts", ".d.mts", ".d.cts", ".ts", ".tsx", ".mts", ".cts", ".js", ".jsx"])
        {
            if (fileName.EndsWith(extension, StringComparison.Ordinal))
            {
                return fileName[..^extension.Length];
            }
        }
        return fileName;
    }

    // checker.ts symbolToEntityNameNode
    private Node SymbolToEntityNameNode(SymbolRef symbol)
    {
        var identifier = SIdentifier(Utilities.UnescapeLeadingUnderscores(NameOf(symbol)));
        var parent = ParentOf(symbol);
        return !parent.IsNone ? SQualifiedName(SymbolToEntityNameNode(parent), identifier) : identifier;
    }

    // checker.ts symbolToTypeNode
    private Node SymbolToTypeNode(SymbolRef symbol, NodeBuilderContext context, SymbolFlags meaning, List<Node>? overrideTypeArguments = null)
    {
        var chain = LookupSymbolChain(symbol, context, meaning, (context.Flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) == 0); // If we're using aliases outside the current scope, dont bother with the module

        var isTypeOf = meaning == SymbolFlags.Value;
        if ((DeclarationsOf(chain[0]) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
        {
            // module is root, must use `ImportTypeNode`
            var nonRootParts = chain.Count > 1 ? CreateAccessFromSymbolChain(chain, chain.Count - 1, 1) : Node.None;
            var typeParameterNodes = overrideTypeArguments ?? LookupTypeParameterNodes(chain, 0, context);
            // (resolution-mode import attributes under node16/nodenext module resolution: not reached with Gate C's options)
            var specifier = GetSpecifierForModuleSymbol(chain[0], context);
            var lit = SLiteralType(SStringLiteral(specifier));
            context.ApproximateLength += specifier.Length + 10; // specifier + import("")
            if (nonRootParts.IsNone || S.Kind(nonRootParts) is SyntaxKind.Identifier or SyntaxKind.QualifiedName)
            {
                if (!nonRootParts.IsNone)
                {
                    var lastId = S.Kind(nonRootParts) == SyntaxKind.Identifier ? nonRootParts : S.Right(nonRootParts);
                    SetIdentifierTypeArguments(lastId, NodeList.None);
                }
                return SImportType(lit, Node.None, nonRootParts, SList(typeParameterNodes), isTypeOf);
            }
            var splitNode = GetTopmostIndexedAccessType(nonRootParts);
            var qualifier = S.TypeName(S.ObjectType(splitNode));
            return SIndexedAccessType(SImportType(lit, Node.None, qualifier, SList(typeParameterNodes), isTypeOf), S.IndexType(splitNode));
        }

        var entityName = CreateAccessFromSymbolChain(chain, chain.Count - 1, 0);
        if (S.Kind(entityName) == SyntaxKind.IndexedAccessType)
        {
            return entityName; // Indexed accesses can never be `typeof`
        }
        if (isTypeOf)
        {
            return STypeQuery(entityName);
        }
        var last = S.Kind(entityName) == SyntaxKind.Identifier ? entityName : S.Right(entityName);
        var lastTypeArgs = GetIdentifierTypeArguments(last);
        SetIdentifierTypeArguments(last, NodeList.None);
        return STypeReference(entityName, lastTypeArgs);

        Node CreateAccessFromSymbolChain(List<SymbolRef> chain, int index, int stopper)
        {
            var typeParameterNodes = index == chain.Count - 1 ? overrideTypeArguments : LookupTypeParameterNodes(chain, index, context);
            var symbol = chain[index];
            var parent = index > 0 ? chain[index - 1] : SymbolRef.None;

            string? symbolName = null;
            if (index == 0)
            {
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
                symbolName = GetNameOfSymbolAsWritten(symbol, context);
                context.ApproximateLength += symbolName.Length + 1;
                context.Flags ^= NodeBuilderFlags.InInitialEntityName;
            }
            else if (!parent.IsNone)
            {
                var exports = GetExportsOfSymbol(parent);
                for (var i = 0; i < exports.Count; i++)
                {
                    var name = exports.NameAt(i);
                    if (!GetSymbolIfSameReference(exports.ValueAt(i), symbol).IsNone && !IsLateBoundName(name) && name != InternalSymbolName.ExportEquals)
                    {
                        symbolName = Utilities.UnescapeLeadingUnderscores(name);
                        break;
                    }
                }
            }

            if (symbolName is null)
            {
                var nameNode = NodeRef.None;
                foreach (var d in DeclarationsOf(symbol) ?? [])
                {
                    nameNode = GetNameOfDeclarationRef(d);
                    if (!nameNode.IsNone)
                    {
                        break;
                    }
                }
                if (!nameNode.IsNone && Kind(nameNode) == SyntaxKind.ComputedPropertyName && IsEntityNameNode(StoreOf(nameNode).Expression(nameNode.Node), StoreOf(nameNode)))
                {
                    var lhs = CreateAccessFromSymbolChain(chain, index - 1, stopper);
                    if (S.Kind(lhs) is SyntaxKind.Identifier or SyntaxKind.QualifiedName)
                    {
                        return SIndexedAccessType(SParenthesizedType(STypeQuery(lhs)), STypeQuery(CopyExistingEntityName(At(nameNode, StoreOf(nameNode).Expression(nameNode.Node)))));
                    }
                    return lhs;
                }
                symbolName = GetNameOfSymbolAsWritten(symbol, context);
            }
            context.ApproximateLength += symbolName.Length + 1;

            if ((context.Flags & NodeBuilderFlags.ForbidIndexedAccessSymbolReferences) == 0 && !parent.IsNone)
            {
                var member = GetMembersOfSymbol(parent).Get(NameOf(symbol));
                if (!member.IsNone && !GetSymbolIfSameReference(member, symbol).IsNone)
                {
                    // Should use an indexed access
                    var lhs = CreateAccessFromSymbolChain(chain, index - 1, stopper);
                    if (S.Kind(lhs) == SyntaxKind.IndexedAccessType)
                    {
                        return SIndexedAccessType(lhs, SLiteralType(SStringLiteral(symbolName)));
                    }
                    return SIndexedAccessType(STypeReference(lhs, SList(typeParameterNodes)), SLiteralType(SStringLiteral(symbolName)));
                }
            }

            var identifier = SetEmitFlags(SIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
            if (typeParameterNodes is not null)
            {
                SetIdentifierTypeArguments(identifier, SList(typeParameterNodes));
            }

            if (index > stopper)
            {
                var lhs = CreateAccessFromSymbolChain(chain, index - 1, stopper);
                if (S.Kind(lhs) is not (SyntaxKind.Identifier or SyntaxKind.QualifiedName))
                {
                    throw new InvalidOperationException("Impossible construct - an export of an indexed access cannot be reachable");
                }
                return SQualifiedName(lhs, identifier);
            }
            return identifier;
        }
    }

    private static bool IsEntityNameNode(Node node, NodeStore store) => store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.QualifiedName;

    // Given A[B][C][D], finds A[B]
    private Node GetTopmostIndexedAccessType(Node top) =>
        S.Kind(S.ObjectType(top)) == SyntaxKind.IndexedAccessType ? GetTopmostIndexedAccessType(S.ObjectType(top)) : top;

    // checker.ts typeParameterShadowsOtherTypeParameterInScope
    private bool TypeParameterShadowsOtherTypeParameterInScope(string escapedName, NodeBuilderContext context, Type type)
    {
        var result = ResolveNameInScope(context.EnclosingDeclaration, context.FakeScope, escapedName, SymbolFlags.Type);
        if (!result.IsNone && (SymbolFlagsOf(result) & SymbolFlags.TypeParameter) != 0)
        {
            return result != Types.Symbol(type);
        }
        return false;
    }

    // checker.ts typeParameterToName
    private Node TypeParameterToName(Type type, NodeBuilderContext context)
    {
        if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && context.TypeParameterNames is not null)
        {
            if (context.TypeParameterNames.TryGetValue(type, out var cached))
            {
                return cached;
            }
        }
        var symbol = Types.Symbol(type);
        var result = SymbolToName(symbol, context, SymbolFlags.Type, expectsIdentifier: true);
        if (S.Kind(result) != SyntaxKind.Identifier)
        {
            return SIdentifier("(Missing type parameter)");
        }
        var declarations = symbol.IsNone ? null : DeclarationsOf(symbol);
        var decl = declarations is { Count: > 0 } d ? d[0] : NodeRef.None;
        if (!decl.IsNone && Kind(decl) == SyntaxKind.TypeParameter)
        {
            result = SetTextRange(context, result, At(decl, StoreOf(decl).Name(decl.Node)));
        }
        if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
        {
            var rawtext = S.Texts.GetString(S.EscapedText(result)) ?? "";
            var i = context.TypeParameterNamesByTextNextNameCount?.GetValueOrDefault(rawtext) ?? 0;
            var text = rawtext;
            while ((context.TypeParameterNamesByText is not null && context.TypeParameterNamesByText.Contains(text)) || TypeParameterShadowsOtherTypeParameterInScope(text, context, type))
            {
                i++;
                text = rawtext + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (text != rawtext)
            {
                var typeArguments = GetIdentifierTypeArguments(result);
                result = SIdentifier(Utilities.UnescapeLeadingUnderscores(text));
                SetIdentifierTypeArguments(result, typeArguments);
            }
            if (context.MustCreateTypeParametersNamesLookups)
            {
                context.MustCreateTypeParametersNamesLookups = false;
                context.TypeParameterNames = context.TypeParameterNames is null ? [] : new Dictionary<Type, Node>(context.TypeParameterNames);
                context.TypeParameterNamesByTextNextNameCount = context.TypeParameterNamesByTextNextNameCount is null ? new Dictionary<string, int>(StringComparer.Ordinal) : new Dictionary<string, int>(context.TypeParameterNamesByTextNextNameCount, StringComparer.Ordinal);
                context.TypeParameterNamesByText = context.TypeParameterNamesByText is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(context.TypeParameterNamesByText, StringComparer.Ordinal);
            }
            // avoiding iterations of the above loop turns out to be worth it when `i` starts to get large, so we cache the max
            // `i` we've used thus far, to save work later
            context.TypeParameterNamesByTextNextNameCount![rawtext] = i;
            context.TypeParameterNames![type] = result;
            context.TypeParameterNamesByText!.Add(text);
        }
        return result;
    }

    // checker.ts symbolToName
    private Node SymbolToName(SymbolRef symbol, NodeBuilderContext context, SymbolFlags meaning, bool expectsIdentifier)
    {
        var chain = LookupSymbolChain(symbol, context, meaning);
        if (expectsIdentifier && chain.Count != 1
            && !context.EncounteredError
            && (context.Flags & NodeBuilderFlags.AllowQualifiedNameInPlaceOfIdentifier) == 0)
        {
            context.EncounteredError = true;
        }
        return CreateEntityNameFromSymbolChain(chain.Count - 1);

        Node CreateEntityNameFromSymbolChain(int index)
        {
            var typeParameterNodes = LookupTypeParameterNodes(chain, index, context);
            var symbol = chain[index];
            if (index == 0)
            {
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
            }
            var symbolName = GetNameOfSymbolAsWritten(symbol, context);
            if (index == 0)
            {
                context.Flags ^= NodeBuilderFlags.InInitialEntityName;
            }
            var identifier = SetEmitFlags(SIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
            if (typeParameterNodes is not null)
            {
                SetIdentifierTypeArguments(identifier, SList(typeParameterNodes));
            }
            return index > 0 ? SQualifiedName(CreateEntityNameFromSymbolChain(index - 1), identifier) : identifier;
        }
    }

    // checker.ts symbolToExpression
    private Node SymbolToExpression(SymbolRef symbol, NodeBuilderContext context, SymbolFlags meaning)
    {
        var chain = LookupSymbolChain(symbol, context, meaning);
        return CreateExpressionFromSymbolChain(chain.Count - 1);

        Node CreateExpressionFromSymbolChain(int index)
        {
            var typeParameterNodes = LookupTypeParameterNodes(chain, index, context);
            var symbol = chain[index];
            if (index == 0)
            {
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
            }
            var symbolName = GetNameOfSymbolAsWritten(symbol, context);
            if (index == 0)
            {
                context.Flags ^= NodeBuilderFlags.InInitialEntityName;
            }
            var firstChar = symbolName.Length > 0 ? symbolName[0] : '\0';
            if (firstChar is '"' or '\'' && (DeclarationsOf(symbol) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
            {
                var specifier = GetSpecifierForModuleSymbol(symbol, context);
                context.ApproximateLength += 2 + specifier.Length; // "specifier"
                return SStringLiteral(specifier);
            }
            if (index == 0 || CanUsePropertyAccess(symbolName))
            {
                var identifier = SetEmitFlags(SIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
                if (typeParameterNodes is not null)
                {
                    SetIdentifierTypeArguments(identifier, SList(typeParameterNodes));
                }
                context.ApproximateLength += 1 + symbolName.Length; // .symbolName
                return index > 0 ? SPropertyAccessExpression(CreateExpressionFromSymbolChain(index - 1), identifier) : identifier;
            }
            if (firstChar == '[')
            {
                symbolName = symbolName[1..^1];
                firstChar = symbolName.Length > 0 ? symbolName[0] : '\0';
            }
            var expression = Node.None;
            if (firstChar is '"' or '\'' && (SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) == 0)
            {
                var literalText = UnescapeBackslashes(StripQuotes(symbolName));
                context.ApproximateLength += literalText.Length + 2; // "literalText"
                expression = SStringLiteral(literalText, firstChar == '\'');
            }
            else if (IsNumericLiteralName(symbolName))
            {
                context.ApproximateLength += symbolName.Length; // +symbolName
                expression = SNumericLiteral(JsNumber.FromString(symbolName));
            }
            if (expression.IsNone)
            {
                var identifier = SetEmitFlags(SIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
                if (typeParameterNodes is not null)
                {
                    SetIdentifierTypeArguments(identifier, SList(typeParameterNodes));
                }
                context.ApproximateLength += symbolName.Length; // symbolName
                expression = identifier;
            }
            context.ApproximateLength += 2; // []
            return SElementAccessExpression(CreateExpressionFromSymbolChain(index - 1), expression);
        }
    }

    // utilities.ts stripQuotes
    private static string StripQuotes(string name)
    {
        var length = name.Length;
        if (length >= 2 && name[0] == name[length - 1] && name[0] is '"' or '\'' or '`')
        {
            return name[1..^1];
        }
        return name;
    }

    // .replace(/\\./g, s => s.substring(1))
    private static string UnescapeBackslashes(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && !Scanner.IsLineBreak(text[i + 1]))
            {
                sb.Append(text[i + 1]);
                i++;
                continue;
            }
            sb.Append(text[i]);
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

    // checker.ts symbolToNode
    private Node SymbolToNode(SymbolRef symbol, NodeBuilderContext context, SymbolFlags meaning)
    {
        if ((context.InternalFlags & InternalNodeBuilderFlags.WriteComputedProps) != 0)
        {
            var valueDeclaration = ValueDeclarationOf(symbol);
            if (!valueDeclaration.IsNone)
            {
                var name = GetNameOfDeclarationRef(valueDeclaration);
                if (!name.IsNone && Kind(name) == SyntaxKind.ComputedPropertyName)
                {
                    return CopyExistingNode(name);
                }
            }
            var nameType = GetNameTypeOfSymbol(symbol);
            if (!nameType.IsNone && (Types.Flags(nameType) & (TypeFlags.EnumLiteral | TypeFlags.UniqueESSymbol)) != 0)
            {
                context.EnclosingDeclaration = ValueDeclarationOf(Types.Symbol(nameType));
                context.FakeScope = null;
                return SComputedPropertyName(SymbolToExpression(Types.Symbol(nameType), context, meaning));
            }
        }
        return SymbolToExpression(symbol, context, meaning);
    }

    // checker.ts getNameOfSymbolAsWritten
    private string GetNameOfSymbolAsWritten(SymbolRef symbol, NodeBuilderContext? context = null)
    {
        if (context?.RemappedSymbolReferences is { } remapped && remapped.TryGetValue(symbol, out var remappedSymbol))
        {
            symbol = remappedSymbol;
        }
        var declarations = DeclarationsOf(symbol);
        if (context is not null && NameOf(symbol) == InternalSymbolName.Default && (context.Flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) == 0 &&
            // If it's not the first part of an entity name, it must print as `default`
            ((context.Flags & NodeBuilderFlags.InInitialEntityName) == 0 ||
                // if the symbol is synthesized, it will only be referenced externally it must print as `default`
                declarations is null ||
                // if not in the same binding context (source file, module declaration), it must print as `default`
                (!context.EnclosingDeclaration.IsNone && FindDefaultBindingContext(declarations.Value[0]) != FindDefaultBindingContext(context.EnclosingDeclaration))))
        {
            return "default";
        }
        if (declarations is { Count: > 0 } list)
        {
            var declaration = NodeRef.None;
            var name = NodeRef.None;
            foreach (var d in list)
            {
                // Try using a declaration with a name, first
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
                if (Kind(declaration) == SyntaxKind.CallExpression && Utilities.IsBindableObjectDefinePropertyCall(StoreOf(declaration), declaration.Node))
                {
                    return SymbolName(symbol);
                }
                if (Kind(name) == SyntaxKind.ComputedPropertyName && (GetCheckFlags(symbol) & CheckFlags.Late) == 0)
                {
                    var nameType = GetNameTypeOfSymbol(symbol);
                    if (!nameType.IsNone && (Types.Flags(nameType) & TypeFlags.StringOrNumberLiteral) != 0)
                    {
                        // Computed property name isn't late bound, but has a well-known name type - use name type to generate a symbol name
                        var result = GetNameOfSymbolFromNameType(symbol, context);
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                }
                return Utilities.DeclarationNameToString(StoreOf(name), name.Node);
            }
            declaration = list[0]; // Declaration may be nameless, but we'll try anyway
            var parent = Parent(declaration);
            if (!parent.IsNone && Kind(parent) == SyntaxKind.VariableDeclaration)
            {
                var store = StoreOf(parent);
                return Utilities.DeclarationNameToString(store, store.Name(parent.Node));
            }
            switch (Kind(declaration))
            {
                case SyntaxKind.ClassExpression:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                    if (context is not null && !context.EncounteredError && (context.Flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0)
                    {
                        context.EncounteredError = true;
                    }
                    return Kind(declaration) == SyntaxKind.ClassExpression ? "(Anonymous class)" : "(Anonymous function)";
            }
        }
        return GetNameOfSymbolFromNameType(symbol, context) ?? SymbolName(symbol);
    }

    // checker.ts getNameOfSymbolFromNameType
    private string? GetNameOfSymbolFromNameType(SymbolRef symbol, NodeBuilderContext? context)
    {
        var nameType = GetNameTypeOfSymbol(symbol);
        if (!nameType.IsNone)
        {
            if ((Types.Flags(nameType) & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var name = LiteralValueToString(nameType);
                if (!Scanner.IsIdentifierText(name, languageVersion) && !IsNumericLiteralName(name))
                {
                    return "\"" + PrinterUtilities.EscapeString(name, '"') + "\"";
                }
                if (IsNumericLiteralName(name) && name.StartsWith('-'))
                {
                    return "[" + name + "]";
                }
                return name;
            }
            if ((Types.Flags(nameType) & TypeFlags.UniqueESSymbol) != 0)
            {
                return "[" + GetNameOfSymbolAsWritten(Types.Symbol(nameType), context) + "]";
            }
        }
        return null;
    }

    private string LiteralValueToString(Type literalType)
    {
        var value = Types.Literal(literalType).Value;
        return value is double d ? JsNumber.ToString(d) : (string)value!;
    }

    // checker.ts isDefaultBindingContext ancestor: a source file or an ambient module
    private NodeRef FindDefaultBindingContext(NodeRef node)
    {
        for (var n = node; !n.IsNone; n = Parent(n))
        {
            if (Kind(n) == SyntaxKind.SourceFile || Utilities.IsAmbientModule(StoreOf(n), n.Node))
            {
                return n;
            }
        }
        return NodeRef.None;
    }

    // checker.ts isStringNamed
    private bool IsStringNamed(NodeRef d)
    {
        var name = GetNameOfDeclarationRef(d);
        if (name.IsNone)
        {
            return false;
        }
        var store = StoreOf(name);
        switch (store.Kind(name.Node))
        {
            case SyntaxKind.ComputedPropertyName:
                return (Types.Flags(CheckExpressionForNameType(At(name, store.Expression(name.Node)))) & TypeFlags.StringLike) != 0;
            case SyntaxKind.ElementAccessExpression:
                return (Types.Flags(CheckExpressionForNameType(At(name, store.ArgumentExpression(name.Node)))) & TypeFlags.StringLike) != 0;
            default:
                return store.Kind(name.Node) == SyntaxKind.StringLiteral;
        }
    }

    // checker.ts isSingleQuotedStringNamed
    private bool IsSingleQuotedStringNamed(NodeRef d)
    {
        var name = GetNameOfDeclarationRef(d);
        if (name.IsNone)
        {
            return false;
        }
        var store = StoreOf(name);
        if (store.Kind(name.Node) != SyntaxKind.StringLiteral)
        {
            return false;
        }
        if (store.SingleQuote(name.Node))
        {
            return true;
        }
        var text = Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, name.Node), name.Node);
        return text.StartsWith('\'');
    }

    // checker.ts getPropertyNameNodeForSymbol
    private Node GetPropertyNameNodeForSymbol(SymbolRef symbol, NodeBuilderContext context)
    {
        var hashPrivateName = GetClonedHashPrivateName(symbol);
        if (!hashPrivateName.IsNone)
        {
            return hashPrivateName;
        }
        var declarations = DeclarationsOf(symbol);
        var stringNamed = declarations is { Count: > 0 } && declarations.TrueForAll(IsStringNamed);
        var singleQuote = declarations is { Count: > 0 } && declarations.TrueForAll(IsSingleQuotedStringNamed);
        var isMethod = (SymbolFlagsOf(symbol) & SymbolFlags.Method) != 0;
        var fromNameType = GetPropertyNameNodeForSymbolFromNameType(symbol, context, singleQuote, stringNamed, isMethod);
        if (!fromNameType.IsNone)
        {
            return fromNameType;
        }
        var rawName = Utilities.UnescapeLeadingUnderscores(NameOf(symbol));
        return CreatePropertyNameNodeForIdentifierOrLiteral(rawName, singleQuote, stringNamed, isMethod);
    }

    // checker.ts getPropertyNameNodeForSymbolFromNameType
    private Node GetPropertyNameNodeForSymbolFromNameType(SymbolRef symbol, NodeBuilderContext context, bool singleQuote, bool stringNamed, bool isMethod)
    {
        var nameType = GetNameTypeOfSymbol(symbol);
        if (!nameType.IsNone)
        {
            if ((Types.Flags(nameType) & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var name = LiteralValueToString(nameType);
                if (!Scanner.IsIdentifierText(name, languageVersion) && (stringNamed || !IsNumericLiteralName(name)))
                {
                    return SStringLiteral(name, singleQuote);
                }
                if (IsNumericLiteralName(name) && name.StartsWith('-'))
                {
                    return SComputedPropertyName(SPrefixUnaryExpression(SyntaxKind.MinusToken, SNumericLiteral(-JsNumber.FromString(name))));
                }
                return CreatePropertyNameNodeForIdentifierOrLiteral(name, singleQuote, stringNamed, isMethod);
            }
            if ((Types.Flags(nameType) & TypeFlags.UniqueESSymbol) != 0)
            {
                return SComputedPropertyName(SymbolToExpression(Types.Symbol(nameType), context, SymbolFlags.Value));
            }
        }
        return Node.None;
    }

    // utilities.ts createPropertyNameNodeForIdentifierOrLiteral
    private Node CreatePropertyNameNodeForIdentifierOrLiteral(string name, bool singleQuote, bool stringNamed, bool isMethod)
    {
        var isMethodNamedNew = isMethod && name == "new";
        return !isMethodNamedNew && Scanner.IsIdentifierText(name, languageVersion) ? SIdentifier(name) :
            !stringNamed && !isMethodNamedNew && IsNumericLiteralName(name) && JsNumber.FromString(name) >= 0 ? SNumericLiteral(JsNumber.FromString(name)) :
            SStringLiteral(name, singleQuote);
    }

    // utilities.ts isNumericLiteralName: (+name).toString() === name
    // utilities.ts isNumericLiteralName: (+name).toString() === name (a leading '-' and Infinity included, module C4)
    private static bool IsNumericLiteralName(string name)
    {
        var number = name switch
        {
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => name.Length > 1 && name[0] == '-' ? -JsNumber.FromString(name[1..]) : JsNumber.FromString(name),
        };
        return JsNumber.ToString(number) == name;
    }

    // checker.ts getClonedHashPrivateName
    private Node GetClonedHashPrivateName(SymbolRef symbol)
    {
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (valueDeclaration.IsNone)
        {
            return Node.None;
        }
        var name = GetNameOfDeclarationRef(valueDeclaration);
        if (!name.IsNone && Kind(name) == SyntaxKind.PrivateIdentifier && UtilitiesPublic.IsNamedDeclaration(StoreOf(valueDeclaration), valueDeclaration.Node))
        {
            // factory.cloneNode: reused
            return CopyShell(name);
        }
        return Node.None;
    }

    // checker.ts addSymbolTypeToContext
    private static Action AddSymbolTypeToContext(NodeBuilderContext context, SymbolRef symbol, Type type)
    {
        var hadOld = context.EnclosingSymbolTypes.TryGetValue(symbol, out var oldType);
        context.EnclosingSymbolTypes[symbol] = type;
        return () =>
        {
            if (hadOld && !oldType.IsNone)
            {
                context.EnclosingSymbolTypes[symbol] = oldType;
            }
            else
            {
                context.EnclosingSymbolTypes.Remove(symbol);
            }
        };
    }

    // checker.ts serializeInferredTypeForDeclaration
    private Node SerializeInferredTypeForDeclaration(SymbolRef symbol, NodeBuilderContext context, Type type)
    {
        if ((Types.Flags(type) & TypeFlags.UniqueESSymbol) != 0 && Types.Symbol(type) == symbol &&
            (context.EnclosingDeclaration.IsNone || (DeclarationsOf(symbol) ?? []).Exists(d => d.File == context.EnclosingFile)))
        {
            context.Flags |= NodeBuilderFlags.AllowUniqueESSymbolType;
        }
        return TypeToTypeNodeHelper(type, context);
    }

    // checker.ts serializeTypeForDeclaration
    private Node SerializeTypeForDeclaration(NodeBuilderContext context, NodeRef declaration, Type type, SymbolRef symbol)
    {
        var result = Node.None;
        var addUndefinedForParameter = !declaration.IsNone && Kind(declaration) is SyntaxKind.Parameter or SyntaxKind.JSDocParameterTag && RequiresAddingImplicitUndefined(declaration, context.EnclosingDeclaration);
        var decl = declaration;
        if (decl.IsNone)
        {
            decl = ValueDeclarationOf(symbol);
        }
        if (decl.IsNone)
        {
            decl = GetDeclarationWithTypeAnnotation(symbol);
        }
        if (decl.IsNone)
        {
            var declarations = DeclarationsOf(symbol);
            decl = declarations is { Count: > 0 } d ? d[0] : NodeRef.None;
        }
        if (!CanPossiblyExpandType(type, context) && !decl.IsNone)
        {
            var restore = AddSymbolTypeToContext(context, symbol, type);
            if (Kind(decl) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
            {
                result = SerializeTypeOfAccessor(decl, symbol, context);
            }
            else if (HasInferredType(decl) && (Types.ObjectFlagsOf(type) & ObjectFlags.RequiresWidening) == 0)
            {
                result = SerializeTypeOfDeclarationSyntactic(decl, symbol, context);
            }
            restore();
        }
        if (result.IsNone)
        {
            if (addUndefinedForParameter)
            {
                type = GetOptionalType(type);
            }
            result = SerializeInferredTypeForDeclaration(symbol, context, type);
        }
        return !result.IsNone ? result : SKeyword(SyntaxKind.AnyKeyword);
    }

    // checker.ts getDeclarationWithTypeAnnotation
    private NodeRef GetDeclarationWithTypeAnnotation(SymbolRef symbol)
    {
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            if (!GetNonlocalEffectiveTypeAnnotationNode(d).IsNone)
            {
                return d;
            }
        }
        return NodeRef.None;
    }

    // utilities.ts hasInferredType
    private static bool HasInferredTypeKind(SyntaxKind kind) => kind is
        SyntaxKind.Parameter or SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration or SyntaxKind.BindingElement or
        SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.BinaryExpression or SyntaxKind.VariableDeclaration or
        SyntaxKind.ExportAssignment or SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.JSDocParameterTag or
        SyntaxKind.JSDocPropertyTag;

    private bool HasInferredType(NodeRef node) => HasInferredTypeKind(Kind(node)) && (Kind(node) != SyntaxKind.BinaryExpression || IsInferrableBinaryExpression(node));

    // utilities.ts hasInferredType's BinaryExpression case: an assignment declaration (JavaScript expando properties: C9)
    private bool IsInferrableBinaryExpression(NodeRef node) => Utilities.GetAssignmentDeclarationKind(StoreOf(node), node.Node) != AssignmentDeclarationKind.None;

    // checker.ts typeNodeIsEquivalentToType
    private bool TypeNodeIsEquivalentToType(NodeRef annotatedDeclaration, Type type, Type typeFromTypeNode)
    {
        if (typeFromTypeNode == type)
        {
            return true;
        }
        if (annotatedDeclaration.IsNone)
        {
            return false;
        }
        var store = StoreOf(annotatedDeclaration);
        var kind = store.Kind(annotatedDeclaration.Node);
        if (kind is SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration && !store.QuestionToken(annotatedDeclaration.Node).IsNone)
        {
            return GetTypeWithFactsNEUndefined(type) == typeFromTypeNode;
        }
        if (kind == SyntaxKind.Parameter && HasEffectiveQuestionToken(annotatedDeclaration))
        {
            return GetTypeWithFactsNEUndefined(type) == typeFromTypeNode;
        }
        return false;
    }

    // checker.ts hasEffectiveQuestionToken
    private bool HasEffectiveQuestionToken(NodeRef node)
    {
        var store = StoreOf(node);
        return (store.Kind(node.Node) != SyntaxKind.JSDocParameterTag && store.Kind(node.Node) != SyntaxKind.JSDocPropertyTag && !store.QuestionToken(node.Node).IsNone) ||
            IsOptionalJSDocPropertyLikeTag(store, node.Node) ||
            (store.Kind(node.Node) == SyntaxKind.Parameter && IsJSDocOptionalParameter(store, node.Node));
    }

    // checker.ts serializeReturnTypeForSignature
    private Node SerializeReturnTypeForSignature(NodeBuilderContext context, Signature signature)
    {
        var suppressAny = (context.Flags & NodeBuilderFlags.SuppressAnyReturnType) != 0;
        var saved = SaveRestoreFlags(context);
        if (suppressAny)
        {
            context.Flags &= ~NodeBuilderFlags.SuppressAnyReturnType; // suppress only toplevel `any`s
        }
        var returnTypeNode = Node.None;
        var returnType = GetReturnTypeOfSignature(signature);
        if (!(suppressAny && (Types.Flags(returnType) & TypeFlags.Any) != 0))
        {
            var declaration = Signatures.Record(signature).Declaration;
            if (!declaration.IsNone && !CanPossiblyExpandType(returnType, context))
            {
                var declarationSymbol = GetSymbolOfDeclaration(declaration);
                var restore = AddSymbolTypeToContext(context, declarationSymbol, returnType);
                returnTypeNode = SerializeReturnTypeForSignatureSyntactic(declaration, declarationSymbol, context);
                restore();
            }
            if (returnTypeNode.IsNone)
            {
                returnTypeNode = SerializeInferredReturnTypeForSignature(context, signature, returnType);
            }
        }
        if (returnTypeNode.IsNone && !suppressAny)
        {
            returnTypeNode = SKeyword(SyntaxKind.AnyKeyword);
        }
        saved.Restore(context);
        return returnTypeNode;
    }

    // checker.ts serializeInferredReturnTypeForSignature
    private Node SerializeInferredReturnTypeForSignature(NodeBuilderContext context, Signature signature, Type returnType)
    {
        var oldSuppressReportInferenceFallback = context.SuppressReportInferenceFallback;
        context.SuppressReportInferenceFallback = true;
        var typePredicate = GetTypePredicateOfSignature(signature);
        var returnTypeNode = typePredicate is not null
            ? TypePredicateToTypePredicateNodeHelper(!context.Mapper.IsNone ? InstantiateTypePredicate(typePredicate, context.Mapper) : typePredicate, context)
            : TypeToTypeNodeHelper(returnType, context);
        context.SuppressReportInferenceFallback = oldSuppressReportInferenceFallback;
        return returnTypeNode;
    }
}
