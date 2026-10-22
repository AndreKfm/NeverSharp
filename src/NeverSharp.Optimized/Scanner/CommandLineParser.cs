// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/commandLineParser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tsoptions/declscompiler.go and internal/tsoptions/enummaps.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>commandLineParser.ts.</summary>
internal static partial class CommandLineParser
{
    /// <summary>
    /// commandLineParser.ts: targetOptionDeclaration, the part that getNameOfScriptTarget reads (its `type`
    /// map; the other members of the CommandLineOptionOfCustomType describe the command line option for
    /// tsc's option parsing and help output, which the scanner and parser do not reach).
    /// </summary>
    internal static readonly TargetOptionDeclarationType TargetOptionDeclaration = new();

    /// <summary>The value of targetOptionDeclaration (CommandLineOptionOfCustomType), reduced to the members in scope.</summary>
    internal sealed class TargetOptionDeclarationType
    {
        internal string Name { get; } = "target";

        internal string ShortName { get; } = "t";

        // `new Map(Object.entries({...}))`: a Map iterates in insertion order (es6 before es2015).
        internal OrderedDictionary<string, ScriptTarget> Type { get; } = new(StringComparer.Ordinal)
        {
            ["es3"] = ScriptTarget.ES3,
            ["es5"] = ScriptTarget.ES5,
            ["es6"] = ScriptTarget.ES2015,
            ["es2015"] = ScriptTarget.ES2015,
            ["es2016"] = ScriptTarget.ES2016,
            ["es2017"] = ScriptTarget.ES2017,
            ["es2018"] = ScriptTarget.ES2018,
            ["es2019"] = ScriptTarget.ES2019,
            ["es2020"] = ScriptTarget.ES2020,
            ["es2021"] = ScriptTarget.ES2021,
            ["es2022"] = ScriptTarget.ES2022,
            ["es2023"] = ScriptTarget.ES2023,
            ["es2024"] = ScriptTarget.ES2024,
            ["es2025"] = ScriptTarget.ES2025,
            ["esnext"] = ScriptTarget.ESNext,
        };

        internal bool AffectsSourceFile { get; } = true;

        internal bool AffectsModuleResolution { get; } = true;

        internal bool AffectsEmit { get; } = true;

        internal bool AffectsBuildInfo { get; } = true;

        internal IReadOnlyList<string> DeprecatedKeys { get; } = ["es3", "es5"];

        internal DiagnosticMessage ParamType { get; } = Diagnostics.VERSION;

        internal bool ShowInSimplifiedHelpView { get; } = true;

        internal DiagnosticMessage Category { get; } = Diagnostics.Language_and_Environment;

        internal DiagnosticMessage Description { get; } = Diagnostics.Set_the_JavaScript_language_version_for_emitted_JavaScript_and_include_compatible_library_declarations;

        internal ScriptTarget DefaultValueDescription { get; } = ScriptTarget.LatestStandard;
    }
}
