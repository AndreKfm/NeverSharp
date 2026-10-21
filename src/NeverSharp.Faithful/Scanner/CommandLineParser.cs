// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/commandLineParser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tsoptions/enummaps.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: CommandLineOptionOfCustomType, with the members getNameOfScriptTarget needs (name, shortName, type).
/// The remaining members of targetOptionDeclaration (affects* flags, deprecatedKeys, paramType, category,
/// description, defaultValueDescription) belong to the command line, which the parser does not reach.
/// </summary>
internal sealed class CommandLineOptionOfCustomType
{
    public string Name { get; init; } = null!;

    public string? ShortName { get; init; }

    /// <summary>TypeScript: `type: Map&lt;string, number | string&gt;` (a JavaScript Map keeps insertion order).</summary>
    public OrderedDictionary<string, int> Type { get; init; } = null!;
}

/// <summary>commandLineParser.ts.</summary>
internal static partial class CommandLineParser
{
    // NOTE: We must reevaluate the target for upcoming features when each successive TC39 edition is ratified in
    //       June of each year. This includes changes to `LanguageFeatureMinimumTarget`, `ScriptTarget`,
    //       `ScriptTargetFeatures`, `CommandLineOptionOfCustomType`, transformers/esnext.ts, compiler/commandLineParser.ts,
    //       compiler/utilitiesPublic.ts, and the contents of each lib/esnext.*.d.ts file.
    internal static readonly CommandLineOptionOfCustomType TargetOptionDeclaration = new()
    {
        Name = "target",
        ShortName = "t",
        Type = new OrderedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["es3"] = (int)ScriptTarget.ES3,
            ["es5"] = (int)ScriptTarget.ES5,
            ["es6"] = (int)ScriptTarget.ES2015,
            ["es2015"] = (int)ScriptTarget.ES2015,
            ["es2016"] = (int)ScriptTarget.ES2016,
            ["es2017"] = (int)ScriptTarget.ES2017,
            ["es2018"] = (int)ScriptTarget.ES2018,
            ["es2019"] = (int)ScriptTarget.ES2019,
            ["es2020"] = (int)ScriptTarget.ES2020,
            ["es2021"] = (int)ScriptTarget.ES2021,
            ["es2022"] = (int)ScriptTarget.ES2022,
            ["es2023"] = (int)ScriptTarget.ES2023,
            ["es2024"] = (int)ScriptTarget.ES2024,
            ["es2025"] = (int)ScriptTarget.ES2025,
            ["esnext"] = (int)ScriptTarget.ESNext,
        },
    };
}
