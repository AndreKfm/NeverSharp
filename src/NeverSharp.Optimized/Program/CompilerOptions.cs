// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (CompilerOptions), src/compiler/utilities.ts (computedOptions, getStrictOptionValue,
// moduleResolutionSupportsPackageJsonExportsAndImports) and src/compiler/utilitiesPublic.ts (targetToLibMap,
// getDefaultLibFileName)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/compileroptions.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; only the options the binder, the program layer and the checker read
// (module C0 moved the binder's options class here and extended it; checker modules add what they read).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// types.ts: CompilerOptions. Every option is nullable: null is TypeScript's undefined, and the computed values
/// (utilities.ts computedOptions, the Get* methods below) apply TypeScript 6.0.3's defaults. Gate C (phase3-plan.md)
/// uses <c>{ noResolve: true }</c>; the binder dump and the binder drivers bind with <c>{}</c>.
/// </summary>
internal sealed class CompilerOptions
{
    public bool? AllowArbitraryExtensions { get; set; }

    public bool? AllowImportingTsExtensions { get; set; }

    public bool? AllowJs { get; set; }

    public bool? AllowSyntheticDefaultImports { get; set; }

    public bool? AllowUmdGlobalAccess { get; set; }

    public bool? AllowUnreachableCode { get; set; }

    public bool? AllowUnusedLabels { get; set; }

    public bool? AlwaysStrict { get; set; }

    public bool? CheckJs { get; set; }

    public bool? Composite { get; set; }

    public bool? Declaration { get; set; }

    public bool? DeclarationMap { get; set; }

    public bool? EmitDecoratorMetadata { get; set; }

    public bool? EsModuleInterop { get; set; }

    public bool? ExactOptionalPropertyTypes { get; set; }

    public bool? ExperimentalDecorators { get; set; }

    public bool? Incremental { get; set; }

    public bool? IsolatedModules { get; set; }

    public JsxEmit? Jsx { get; set; }

    public IReadOnlyList<string>? Lib { get; set; }

    public bool? LibReplacement { get; set; }

    public ModuleKind? Module { get; set; }

    public ModuleDetectionKind? ModuleDetection { get; set; }

    public ModuleResolutionKind? ModuleResolution { get; set; }

    public bool? NoFallthroughCasesInSwitch { get; set; }

    public bool? NoImplicitAny { get; set; }

    public bool? NoImplicitOverride { get; set; }

    public bool? NoImplicitReturns { get; set; }

    public bool? NoImplicitThis { get; set; }

    public bool? NoLib { get; set; }

    public bool? NoPropertyAccessFromIndexSignature { get; set; }

    public bool? NoResolve { get; set; }

    public bool? NoUncheckedIndexedAccess { get; set; }

    public bool? NoUnusedLocals { get; set; }

    public bool? NoUnusedParameters { get; set; }

    public bool? PreserveConstEnums { get; set; }

    public bool? ResolveJsonModule { get; set; }

    public bool? ResolvePackageJsonExports { get; set; }

    public bool? ResolvePackageJsonImports { get; set; }

    public bool? RewriteRelativeImportExtensions { get; set; }

    public bool? SkipDefaultLibCheck { get; set; }

    public bool? SkipLibCheck { get; set; }

    public bool? Strict { get; set; }

    public bool? StrictBindCallApply { get; set; }

    public bool? StrictBuiltinIteratorReturn { get; set; }

    public bool? StrictFunctionTypes { get; set; }

    public bool? StrictNullChecks { get; set; }

    public bool? StrictPropertyInitialization { get; set; }

    public ScriptTarget? Target { get; set; }

    public bool? UseDefineForClassFields { get; set; }

    public bool? UseUnknownInCatchVariables { get; set; }

    public bool? VerbatimModuleSyntax { get; set; }

    // utilities.ts: computedOptions (the computeValue functions, in declaration order).

    internal bool GetAllowImportingTsExtensions() => AllowImportingTsExtensions == true || RewriteRelativeImportExtensions == true;

    /// <summary>utilities.ts getEmitScriptTarget: ES3 is not supported any more and treated as undefined.</summary>
    internal ScriptTarget GetEmitScriptTarget() => Target is { } target && target != ScriptTarget.ES3 ? target : ScriptTarget.LatestStandard;

    /// <summary>utilities.ts getEmitModuleKind.</summary>
    internal ModuleKind GetEmitModuleKind()
    {
        if (Module is { } module)
        {
            return module;
        }
        var target = GetEmitScriptTarget();
        if (target == ScriptTarget.ESNext)
        {
            return ModuleKind.ESNext;
        }
        if (target >= ScriptTarget.ES2022)
        {
            return ModuleKind.ES2022;
        }
        if (target >= ScriptTarget.ES2020)
        {
            return ModuleKind.ES2020;
        }
        if (target >= ScriptTarget.ES2015)
        {
            return ModuleKind.ES2015;
        }
        return ModuleKind.CommonJS;
    }

    /// <summary>utilities.ts getEmitModuleResolutionKind.</summary>
    internal ModuleResolutionKind GetEmitModuleResolutionKind()
    {
        if (ModuleResolution is { } moduleResolution)
        {
            return moduleResolution;
        }
        var moduleKind = GetEmitModuleKind();
        switch (moduleKind)
        {
            case ModuleKind.None:
            case ModuleKind.AMD:
            case ModuleKind.UMD:
            case ModuleKind.System:
                return ModuleResolutionKind.Classic;
            case ModuleKind.NodeNext:
                return ModuleResolutionKind.NodeNext;
        }
        if (ModuleKind.Node16 <= moduleKind && moduleKind < ModuleKind.NodeNext)
        {
            return ModuleResolutionKind.Node16;
        }
        return ModuleResolutionKind.Bundler;
    }

    /// <summary>utilities.ts getEmitModuleDetectionKind.</summary>
    internal ModuleDetectionKind GetEmitModuleDetectionKind()
    {
        if (ModuleDetection is { } moduleDetection)
        {
            return moduleDetection;
        }
        var moduleKind = GetEmitModuleKind();
        return ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext ? ModuleDetectionKind.Force : ModuleDetectionKind.Auto;
    }

    internal bool GetIsolatedModules() => IsolatedModules == true || VerbatimModuleSyntax == true;

    internal bool GetESModuleInterop() => EsModuleInterop ?? true;

    internal bool GetAllowSyntheticDefaultImports() => AllowSyntheticDefaultImports ?? true;

    internal bool GetResolvePackageJsonExports()
    {
        var moduleResolution = GetEmitModuleResolutionKind();
        if (!ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            return false;
        }
        if (ResolvePackageJsonExports is { } value)
        {
            return value;
        }
        return moduleResolution is ModuleResolutionKind.Node16 or ModuleResolutionKind.NodeNext or ModuleResolutionKind.Bundler;
    }

    internal bool GetResolvePackageJsonImports()
    {
        var moduleResolution = GetEmitModuleResolutionKind();
        if (!ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            return false;
        }
        if (ResolvePackageJsonImports is { } value)
        {
            return value;
        }
        return moduleResolution is ModuleResolutionKind.Node16 or ModuleResolutionKind.NodeNext or ModuleResolutionKind.Bundler;
    }

    internal bool GetResolveJsonModule()
    {
        if (ResolveJsonModule is { } value)
        {
            return value;
        }
        switch (GetEmitModuleKind())
        {
            // TODO in 6.0: uncomment
            // case ModuleKind.Node16:
            // case ModuleKind.Node18:
            case ModuleKind.Node20:
            case ModuleKind.NodeNext:
                return true;
        }
        return GetEmitModuleResolutionKind() == ModuleResolutionKind.Bundler;
    }

    internal bool GetEmitDeclarations() => Declaration == true || Composite == true;

    internal bool ShouldPreserveConstEnums() => PreserveConstEnums == true || GetIsolatedModules();

    internal bool IsIncrementalCompilation() => Incremental == true || Composite == true;

    internal bool GetAreDeclarationMapsEnabled() => DeclarationMap == true && GetEmitDeclarations();

    internal bool GetAllowJSCompilerOption() => AllowJs ?? CheckJs == true;

    internal bool GetUseDefineForClassFields() => UseDefineForClassFields ?? GetEmitScriptTarget() >= ScriptTarget.ES2022;

    /// <summary>utilities.ts getStrictOptionValue: an unset strict-family flag follows <c>strict</c>, which defaults to on.</summary>
    internal bool GetStrictOptionValue(bool? flag) => flag ?? Strict != false;

    internal bool GetAlwaysStrict() => AlwaysStrict != false;

    /// <summary>
    /// utilities.ts computedOptions: every computed option with its value, in declaration order (the checker dump
    /// writes them into its header; the oracle writes <c>ts.computedOptions[name].computeValue(options)</c>).
    /// </summary>
    internal List<(string Name, object Value)> ComputedOptionValues() =>
    [
        ("allowImportingTsExtensions", GetAllowImportingTsExtensions()),
        ("target", (int)GetEmitScriptTarget()),
        ("module", (int)GetEmitModuleKind()),
        ("moduleResolution", (int)GetEmitModuleResolutionKind()),
        ("moduleDetection", (int)GetEmitModuleDetectionKind()),
        ("isolatedModules", GetIsolatedModules()),
        ("esModuleInterop", GetESModuleInterop()),
        ("allowSyntheticDefaultImports", GetAllowSyntheticDefaultImports()),
        ("resolvePackageJsonExports", GetResolvePackageJsonExports()),
        ("resolvePackageJsonImports", GetResolvePackageJsonImports()),
        ("resolveJsonModule", GetResolveJsonModule()),
        ("declaration", GetEmitDeclarations()),
        ("preserveConstEnums", ShouldPreserveConstEnums()),
        ("incremental", IsIncrementalCompilation()),
        ("declarationMap", GetAreDeclarationMapsEnabled()),
        ("allowJs", GetAllowJSCompilerOption()),
        ("useDefineForClassFields", GetUseDefineForClassFields()),
        ("noImplicitAny", GetStrictOptionValue(NoImplicitAny)),
        ("noImplicitThis", GetStrictOptionValue(NoImplicitThis)),
        ("strictNullChecks", GetStrictOptionValue(StrictNullChecks)),
        ("strictFunctionTypes", GetStrictOptionValue(StrictFunctionTypes)),
        ("strictBindCallApply", GetStrictOptionValue(StrictBindCallApply)),
        ("strictPropertyInitialization", GetStrictOptionValue(StrictPropertyInitialization)),
        ("strictBuiltinIteratorReturn", GetStrictOptionValue(StrictBuiltinIteratorReturn)),
        ("alwaysStrict", GetAlwaysStrict()),
        ("useUnknownInCatchVariables", GetStrictOptionValue(UseUnknownInCatchVariables)),
    ];

    /// <summary>utilities.ts moduleResolutionSupportsPackageJsonExportsAndImports.</summary>
    internal static bool ModuleResolutionSupportsPackageJsonExportsAndImports(ModuleResolutionKind moduleResolution) =>
        (moduleResolution >= ModuleResolutionKind.Node16 && moduleResolution <= ModuleResolutionKind.NodeNext) || moduleResolution == ModuleResolutionKind.Bundler;

    /// <summary>utilitiesPublic.ts getDefaultLibFileName (targetToLibMap).</summary>
    internal string GetDefaultLibFileName() => GetEmitScriptTarget() switch
    {
        ScriptTarget.ESNext => "lib.esnext.full.d.ts",
        ScriptTarget.ES2025 => "lib.es2025.full.d.ts",
        ScriptTarget.ES2024 => "lib.es2024.full.d.ts",
        ScriptTarget.ES2023 => "lib.es2023.full.d.ts",
        ScriptTarget.ES2022 => "lib.es2022.full.d.ts",
        ScriptTarget.ES2021 => "lib.es2021.full.d.ts",
        ScriptTarget.ES2020 => "lib.es2020.full.d.ts",
        ScriptTarget.ES2019 => "lib.es2019.full.d.ts",
        ScriptTarget.ES2018 => "lib.es2018.full.d.ts",
        ScriptTarget.ES2017 => "lib.es2017.full.d.ts",
        ScriptTarget.ES2016 => "lib.es2016.full.d.ts",
        ScriptTarget.ES2015 => "lib.es6.d.ts", // We don't use lib.es2015.full.d.ts due to breaking change.
        _ => "lib.d.ts",
    };
}
