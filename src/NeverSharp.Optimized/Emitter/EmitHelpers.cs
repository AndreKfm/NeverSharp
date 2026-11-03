// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/emitHelpers.ts (createEmitHelperFactory, compareEmitHelpers, the helpers the
// transformers of TypeScript 6.0.3's default options request) and src/compiler/types.ts (EmitHelper)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/helpers.go and internal/printer/emithelpers.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1. Ported helpers: __esDecorate, __runInitializers,
// __setFunctionName, __propKey, __addDisposableResource, __disposeResources, __classPrivateFieldGet/Set/In and
// __rewriteRelativeImportExtension. The helpers of legacy decorators, downlevel targets and the CommonJS-family module
// transformers are outside Phase 4's options and not ported.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts EmitHelper (ScopedEmitHelper | UnscopedEmitHelper).</summary>
internal sealed class EmitHelper
{
    internal EmitHelper(string name, bool scoped, string text, int? priority = null, string? importName = null, EmitHelper[]? dependencies = null)
    {
        Name = name;
        Scoped = scoped;
        Text = text;
        Priority = priority;
        ImportName = importName;
        Dependencies = dependencies;
    }

    internal EmitHelper(string name, bool scoped, Func<Func<string, string>, string> textCallback, int? priority = null, EmitHelper[]? dependencies = null)
    {
        Name = name;
        Scoped = scoped;
        TextCallback = textCallback;
        Priority = priority;
        Dependencies = dependencies;
    }

    /// <summary>A unique name for this helper.</summary>
    internal string Name { get; }

    /// <summary>Indicates whether the helper MUST be emitted in the current scope.</summary>
    internal bool Scoped { get; }

    /// <summary>ES3-compatible raw script text (null when the text is yielded by <see cref="TextCallback"/>).</summary>
    internal string? Text { get; }

    /// <summary>A function yielding the text, given the callback that makes a name file-level unique.</summary>
    internal Func<Func<string, string>, string>? TextCallback { get; }

    /// <summary>Helpers with a higher priority are emitted earlier than other helpers on the node.</summary>
    internal int? Priority { get; }

    /// <summary>The name of the helper to use when importing via `--importHelpers`.</summary>
    internal string? ImportName { get; }

    internal EmitHelper[]? Dependencies { get; }

    /// <summary>emitHelpers.ts compareEmitHelpers.</summary>
    internal static int Compare(EmitHelper x, EmitHelper y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x.Priority == y.Priority)
        {
            return 0;
        }
        if (x.Priority is null)
        {
            return 1;
        }
        if (y.Priority is null)
        {
            return -1;
        }
        return x.Priority.Value.CompareTo(y.Priority.Value);
    }
}

/// <summary>emitHelpers.ts PrivateIdentifierKind (the string values).</summary>
internal static class PrivateIdentifierKind
{
    internal const string Field = "f";
    internal const string Method = "m";
    internal const string Accessor = "a";
}

/// <summary>emitHelpers.ts ESDecorateName: a computed name expression, or an identifier or private identifier.</summary>
internal readonly record struct ESDecorateName(bool Computed, Node Name);

/// <summary>emitHelpers.ts ESDecorateClassElementAccess.</summary>
internal readonly record struct ESDecorateClassElementAccess(bool Get, bool Set);

/// <summary>
/// emitHelpers.ts ESDecorateContext: kind "class" (ESDecorateClassContext: name and metadata) or a class element kind
/// ("method", "getter", "setter", "accessor", "field": ESDecorateClassElementContext).
/// </summary>
internal sealed class ESDecorateContext
{
    internal string Kind = "class";
    internal Node ClassName;
    internal ESDecorateName ElementName;
    internal bool Static;
    internal bool Private;
    internal ESDecorateClassElementAccess Access;
    internal Node Metadata;
}

/// <summary>emitHelpers.ts createEmitHelperFactory(context): the helper factory of a transformation context.</summary>
internal sealed class EmitHelperFactory
{
    private readonly TransformationContext context;

    internal EmitHelperFactory(TransformationContext context)
    {
        this.context = context;
    }

    private NodeFactory factory => context.Factory;

    private NodeStore store => context.Store;

    private NodeList Args(params Node[] args) => factory.CreateNodeArray(args);

    /// <summary>Gets an identifier for the name of an *unscoped* emit helper.</summary>
    internal Node GetUnscopedHelperName(string name)
    {
        return FactoryEmitNode.SetEmitFlags(store, factory.CreateIdentifier(name), EmitFlags.HelperName | EmitFlags.AdviseOnEmitNode);
    }

    // ES Decorators Helpers

    private Node CreateESDecorateClassContextObject(ESDecorateContext contextIn)
    {
        return factory.CreateObjectLiteralExpression(Args(
            factory.CreatePropertyAssignment(factory.CreateIdentifier("kind"), factory.CreateStringLiteral(store.Texts.Add("class"))),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("name"), contextIn.ClassName),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("metadata"), contextIn.Metadata)));
    }

    private Node CreateAccessor(ESDecorateName elementName)
    {
        return elementName.Computed ?
            factory.CreateElementAccessExpression(factory.CreateIdentifier("obj"), elementName.Name) :
            factory.CreatePropertyAccessExpression(factory.CreateIdentifier("obj"), elementName.Name);
    }

    private Node CreateESDecorateClassElementAccessGetMethod(ESDecorateName elementName)
    {
        var accessor = CreateAccessor(elementName);
        return factory.CreatePropertyAssignment(
            factory.CreateIdentifier("get"),
            factory.CreateArrowFunction(
                default,
                default,
                Args(factory.CreateParameterDeclaration(default, default, factory.CreateIdentifier("obj"))),
                default,
                default,
                accessor));
    }

    private Node CreateESDecorateClassElementAccessSetMethod(ESDecorateName elementName)
    {
        var accessor = CreateAccessor(elementName);
        return factory.CreatePropertyAssignment(
            factory.CreateIdentifier("set"),
            factory.CreateArrowFunction(
                default,
                default,
                Args(
                    factory.CreateParameterDeclaration(default, default, factory.CreateIdentifier("obj")),
                    factory.CreateParameterDeclaration(default, default, factory.CreateIdentifier("value"))),
                default,
                default,
                factory.CreateBlock(Args(
                    factory.CreateExpressionStatement(
                        factory.CreateAssignment(accessor, factory.CreateIdentifier("value")))))));
    }

    private Node CreateESDecorateClassElementAccessHasMethod(ESDecorateName elementName)
    {
        var propertyName = elementName.Computed ? elementName.Name :
            store.Kind(elementName.Name) == SyntaxKind.Identifier ? factory.CreateStringLiteralFromNode(elementName.Name) :
            elementName.Name;

        return factory.CreatePropertyAssignment(
            factory.CreateIdentifier("has"),
            factory.CreateArrowFunction(
                default,
                default,
                Args(factory.CreateParameterDeclaration(default, default, factory.CreateIdentifier("obj"))),
                default,
                default,
                factory.CreateBinaryExpression(propertyName, factory.CreateToken(SyntaxKind.InKeyword), factory.CreateIdentifier("obj"))));
    }

    private Node CreateESDecorateClassElementAccessObject(ESDecorateName name, ESDecorateClassElementAccess access)
    {
        var properties = new List<Node> { CreateESDecorateClassElementAccessHasMethod(name) };
        if (access.Get)
        {
            properties.Add(CreateESDecorateClassElementAccessGetMethod(name));
        }
        if (access.Set)
        {
            properties.Add(CreateESDecorateClassElementAccessSetMethod(name));
        }
        return factory.CreateObjectLiteralExpression(factory.CreateNodeArray(properties));
    }

    private Node CreateESDecorateClassElementContextObject(ESDecorateContext contextIn)
    {
        return factory.CreateObjectLiteralExpression(Args(
            factory.CreatePropertyAssignment(factory.CreateIdentifier("kind"), factory.CreateStringLiteral(store.Texts.Add(contextIn.Kind))),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("name"), contextIn.ElementName.Computed ? contextIn.ElementName.Name : factory.CreateStringLiteralFromNode(contextIn.ElementName.Name)),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("static"), factory.CreateToken(contextIn.Static ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword)),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("private"), factory.CreateToken(contextIn.Private ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword)),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("access"), CreateESDecorateClassElementAccessObject(contextIn.ElementName, contextIn.Access)),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("metadata"), contextIn.Metadata)));
    }

    private Node CreateESDecorateContextObject(ESDecorateContext contextIn)
    {
        return contextIn.Kind == "class" ? CreateESDecorateClassContextObject(contextIn) :
            CreateESDecorateClassElementContextObject(contextIn);
    }

    internal Node CreateESDecorateHelper(Node ctor, Node descriptorIn, Node decorators, ESDecorateContext contextIn, Node initializers, Node extraInitializers)
    {
        context.RequestEmitHelper(EmitHelpers.ESDecorateHelper);
        return factory.CreateCallExpression(
            GetUnscopedHelperName("__esDecorate"),
            default,
            Args(
                ctor.IsNone ? factory.CreateToken(SyntaxKind.NullKeyword) : ctor,
                descriptorIn.IsNone ? factory.CreateToken(SyntaxKind.NullKeyword) : descriptorIn,
                decorators,
                CreateESDecorateContextObject(contextIn),
                initializers,
                extraInitializers));
    }

    internal Node CreateRunInitializersHelper(Node thisArg, Node initializers, Node value = default)
    {
        context.RequestEmitHelper(EmitHelpers.RunInitializersHelper);
        return factory.CreateCallExpression(
            GetUnscopedHelperName("__runInitializers"),
            default,
            !value.IsNone ? Args(thisArg, initializers, value) : Args(thisArg, initializers));
    }

    internal Node CreatePropKeyHelper(Node expr)
    {
        context.RequestEmitHelper(EmitHelpers.PropKeyHelper);
        return factory.CreateCallExpression(GetUnscopedHelperName("__propKey"), default, Args(expr));
    }

    internal Node CreateSetFunctionNameHelper(Node f, Node name, string? prefix = null)
    {
        context.RequestEmitHelper(EmitHelpers.SetFunctionNameHelper);
        return context.Factory.CreateCallExpression(
            GetUnscopedHelperName("__setFunctionName"),
            default,
            !string.IsNullOrEmpty(prefix) ? Args(f, name, context.Factory.CreateStringLiteral(store.Texts.Add(prefix))) : Args(f, name));
    }

    // Class Fields Helpers

    internal Node CreateClassPrivateFieldGetHelper(Node receiver, Node state, string kind, Node f)
    {
        context.RequestEmitHelper(EmitHelpers.ClassPrivateFieldGetHelper);
        var args = f.IsNone
            ? Args(receiver, state, factory.CreateStringLiteral(store.Texts.Add(kind)))
            : Args(receiver, state, factory.CreateStringLiteral(store.Texts.Add(kind)), f);
        return factory.CreateCallExpression(GetUnscopedHelperName("__classPrivateFieldGet"), default, args);
    }

    internal Node CreateClassPrivateFieldSetHelper(Node receiver, Node state, Node value, string kind, Node f)
    {
        context.RequestEmitHelper(EmitHelpers.ClassPrivateFieldSetHelper);
        var args = f.IsNone
            ? Args(receiver, state, value, factory.CreateStringLiteral(store.Texts.Add(kind)))
            : Args(receiver, state, value, factory.CreateStringLiteral(store.Texts.Add(kind)), f);
        return factory.CreateCallExpression(GetUnscopedHelperName("__classPrivateFieldSet"), default, args);
    }

    internal Node CreateClassPrivateFieldInHelper(Node state, Node receiver)
    {
        context.RequestEmitHelper(EmitHelpers.ClassPrivateFieldInHelper);
        return factory.CreateCallExpression(GetUnscopedHelperName("__classPrivateFieldIn"), default, Args(state, receiver));
    }

    internal Node CreateAddDisposableResourceHelper(Node envBinding, Node value, bool async)
    {
        context.RequestEmitHelper(EmitHelpers.AddDisposableResourceHelper);
        return factory.CreateCallExpression(
            GetUnscopedHelperName("__addDisposableResource"),
            default,
            Args(envBinding, value, factory.CreateToken(async ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword)));
    }

    internal Node CreateDisposeResourcesHelper(Node envBinding)
    {
        context.RequestEmitHelper(EmitHelpers.DisposeResourcesHelper);
        return factory.CreateCallExpression(GetUnscopedHelperName("__disposeResources"), default, Args(envBinding));
    }

    internal Node CreateRewriteRelativeImportExtensionsHelper(Node expression)
    {
        context.RequestEmitHelper(EmitHelpers.RewriteRelativeImportExtensionsHelper);
        return factory.CreateCallExpression(
            GetUnscopedHelperName("__rewriteRelativeImportExtension"),
            default,
            context.GetCompilerOptions().Jsx == JsxEmit.Preserve
                ? Args(expression, factory.CreateToken(SyntaxKind.TrueKeyword))
                : Args(expression));
    }
}

/// <summary>emitHelpers.ts: the helper objects (texts verbatim from TypeScript 6.0.3).</summary>
internal static class EmitHelpers
{
    internal static readonly EmitHelper ESDecorateHelper = new(
        "typescript:esDecorate",
        scoped: false,
        priority: 2,
        importName: "__esDecorate",
        text: """

        var __esDecorate = (this && this.__esDecorate) || function (ctor, descriptorIn, decorators, contextIn, initializers, extraInitializers) {
            function accept(f) { if (f !== void 0 && typeof f !== "function") throw new TypeError("Function expected"); return f; }
            var kind = contextIn.kind, key = kind === "getter" ? "get" : kind === "setter" ? "set" : "value";
            var target = !descriptorIn && ctor ? contextIn["static"] ? ctor : ctor.prototype : null;
            var descriptor = descriptorIn || (target ? Object.getOwnPropertyDescriptor(target, contextIn.name) : {});
            var _, done = false;
            for (var i = decorators.length - 1; i >= 0; i--) {
                var context = {};
                for (var p in contextIn) context[p] = p === "access" ? {} : contextIn[p];
                for (var p in contextIn.access) context.access[p] = contextIn.access[p];
                context.addInitializer = function (f) { if (done) throw new TypeError("Cannot add initializers after decoration has completed"); extraInitializers.push(accept(f || null)); };
                var result = (0, decorators[i])(kind === "accessor" ? { get: descriptor.get, set: descriptor.set } : descriptor[key], context);
                if (kind === "accessor") {
                    if (result === void 0) continue;
                    if (result === null || typeof result !== "object") throw new TypeError("Object expected");
                    if (_ = accept(result.get)) descriptor.get = _;
                    if (_ = accept(result.set)) descriptor.set = _;
                    if (_ = accept(result.init)) initializers.unshift(_);
                }
                else if (_ = accept(result)) {
                    if (kind === "field") initializers.unshift(_);
                    else descriptor[key] = _;
                }
            }
            if (target) Object.defineProperty(target, contextIn.name, descriptor);
            done = true;
        };
""");

    internal static readonly EmitHelper RunInitializersHelper = new(
        "typescript:runInitializers",
        scoped: false,
        priority: 2,
        importName: "__runInitializers",
        text: """

        var __runInitializers = (this && this.__runInitializers) || function (thisArg, initializers, value) {
            var useValue = arguments.length > 2;
            for (var i = 0; i < initializers.length; i++) {
                value = useValue ? initializers[i].call(thisArg, value) : initializers[i].call(thisArg);
            }
            return useValue ? value : void 0;
        };
""");

    internal static readonly EmitHelper PropKeyHelper = new(
        "typescript:propKey",
        scoped: false,
        importName: "__propKey",
        text: """

        var __propKey = (this && this.__propKey) || function (x) {
            return typeof x === "symbol" ? x : "".concat(x);
        };
""");

    internal static readonly EmitHelper SetFunctionNameHelper = new(
        "typescript:setFunctionName",
        scoped: false,
        importName: "__setFunctionName",
        text: """

        var __setFunctionName = (this && this.__setFunctionName) || function (f, name, prefix) {
            if (typeof name === "symbol") name = name.description ? "[".concat(name.description, "]") : "";
            return Object.defineProperty(f, "name", { configurable: true, value: prefix ? "".concat(prefix, " ", name) : name });
        };
""");

    internal static readonly EmitHelper ClassPrivateFieldGetHelper = new(
        "typescript:classPrivateFieldGet",
        scoped: false,
        importName: "__classPrivateFieldGet",
        text: """

            var __classPrivateFieldGet = (this && this.__classPrivateFieldGet) || function (receiver, state, kind, f) {
                if (kind === "a" && !f) throw new TypeError("Private accessor was defined without a getter");
                if (typeof state === "function" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError("Cannot read private member from an object whose class did not declare it");
                return kind === "m" ? f : kind === "a" ? f.call(receiver) : f ? f.value : state.get(receiver);
            };
""");

    internal static readonly EmitHelper ClassPrivateFieldSetHelper = new(
        "typescript:classPrivateFieldSet",
        scoped: false,
        importName: "__classPrivateFieldSet",
        text: """

            var __classPrivateFieldSet = (this && this.__classPrivateFieldSet) || function (receiver, state, value, kind, f) {
                if (kind === "m") throw new TypeError("Private method is not writable");
                if (kind === "a" && !f) throw new TypeError("Private accessor was defined without a setter");
                if (typeof state === "function" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError("Cannot write private member to an object whose class did not declare it");
                return (kind === "a" ? f.call(receiver, value) : f ? f.value = value : state.set(receiver, value)), value;
            };
""");

    internal static readonly EmitHelper ClassPrivateFieldInHelper = new(
        "typescript:classPrivateFieldIn",
        scoped: false,
        importName: "__classPrivateFieldIn",
        text: """

            var __classPrivateFieldIn = (this && this.__classPrivateFieldIn) || function(state, receiver) {
                if (receiver === null || (typeof receiver !== "object" && typeof receiver !== "function")) throw new TypeError("Cannot use 'in' operator on non-object");
                return typeof state === "function" ? receiver === state : state.has(receiver);
            };
""");

    internal static readonly EmitHelper AddDisposableResourceHelper = new(
        "typescript:addDisposableResource",
        scoped: false,
        importName: "__addDisposableResource",
        text: """

        var __addDisposableResource = (this && this.__addDisposableResource) || function (env, value, async) {
            if (value !== null && value !== void 0) {
                if (typeof value !== "object" && typeof value !== "function") throw new TypeError("Object expected.");
                var dispose, inner;
                if (async) {
                    if (!Symbol.asyncDispose) throw new TypeError("Symbol.asyncDispose is not defined.");
                    dispose = value[Symbol.asyncDispose];
                }
                if (dispose === void 0) {
                    if (!Symbol.dispose) throw new TypeError("Symbol.dispose is not defined.");
                    dispose = value[Symbol.dispose];
                    if (async) inner = dispose;
                }
                if (typeof dispose !== "function") throw new TypeError("Object not disposable.");
                if (inner) dispose = function() { try { inner.call(this); } catch (e) { return Promise.reject(e); } };
                env.stack.push({ value: value, dispose: dispose, async: async });
            }
            else if (async) {
                env.stack.push({ async: true });
            }
            return value;
        };
""");

    internal static readonly EmitHelper DisposeResourcesHelper = new(
        "typescript:disposeResources",
        scoped: false,
        importName: "__disposeResources",
        text: """

        var __disposeResources = (this && this.__disposeResources) || (function (SuppressedError) {
            return function (env) {
                function fail(e) {
                    env.error = env.hasError ? new SuppressedError(e, env.error, "An error was suppressed during disposal.") : e;
                    env.hasError = true;
                }
                var r, s = 0;
                function next() {
                    while (r = env.stack.pop()) {
                        try {
                            if (!r.async && s === 1) return s = 0, env.stack.push(r), Promise.resolve().then(next);
                            if (r.dispose) {
                                var result = r.dispose.call(r.value);
                                if (r.async) return s |= 2, Promise.resolve(result).then(next, function(e) { fail(e); return next(); });
                            }
                            else s |= 1;
                        }
                        catch (e) {
                            fail(e);
                        }
                    }
                    if (s === 1) return env.hasError ? Promise.reject(env.error) : Promise.resolve();
                    if (env.hasError) throw env.error;
                }
                return next();
            };
        })(typeof SuppressedError === "function" ? SuppressedError : function (error, suppressed, message) {
            var e = new Error(message);
            return e.name = "SuppressedError", e.error = error, e.suppressed = suppressed, e;
        });
""");

    internal static readonly EmitHelper RewriteRelativeImportExtensionsHelper = new(
        "typescript:rewriteRelativeImportExtensions",
        scoped: false,
        importName: "__rewriteRelativeImportExtension",
        text: """

        var __rewriteRelativeImportExtension = (this && this.__rewriteRelativeImportExtension) || function (path, preserveJsx) {
            if (typeof path === "string" && /^\.\.?\//.test(path)) {
                return path.replace(/\.(tsx)$|((?:\.d)?)((?:\.[^./]+?)?)\.([cm]?)ts$/i, function (m, tsx, d, ext, cm) {
                    return tsx ? preserveJsx ? ".jsx" : ".js" : d && (!ext || !cm) ? m : (d + ext + "." + cm.toLowerCase() + "js");
                });
            }
            return path;
        };
""");
}
