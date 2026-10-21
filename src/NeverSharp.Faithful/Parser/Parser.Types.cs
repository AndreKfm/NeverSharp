// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>parser.ts: CreateSourceFileOptions.</summary>
internal sealed class CreateSourceFileOptions
{
    public ScriptTarget LanguageVersion { get; set; }

    /// <summary>
    /// Controls the format the file is detected as - this can be derived from only the path
    /// and files on disk, but needs to be done with a module resolution cache in scope to be performant.
    /// This is usually `undefined` for compilations that do not have `moduleResolution` values of `node16` or `nodenext`.
    /// (`ResolutionMode = ModuleKind.ESNext | ModuleKind.CommonJS | undefined`.)
    /// </summary>
    public ModuleKind? ImpliedNodeFormat { get; set; }

    /// <summary>
    /// Controls how module-y-ness is set for the given file. Usually the result of calling
    /// `getSetExternalModuleIndicator` on a valid `CompilerOptions` object. If not present, the default
    /// check specified by `isFileProbablyExternalModule` will be used to set the field.
    /// </summary>
    public Action<SourceFile>? SetExternalModuleIndicator { get; set; }

    public IReadOnlyList<string>? PackageJsonLocations { get; set; }

    /// <summary>`packageJsonScope?: PackageJsonInfo` (checker/program data, not used by the parser).</summary>
    public object? PackageJsonScope { get; set; }

    public JSDocParsingMode? JsDocParsingMode { get; set; }
}

internal sealed partial class Parser
{
    /// <summary>parser.ts: `const enum SignatureFlags` (distinct from types.ts's SignatureFlags of the checker).</summary>
    [Flags]
    internal enum SignatureFlags
    {
        None = 0,
        Yield = 1 << 0,
        Await = 1 << 1,
        Type = 1 << 2,
        IgnoreMissingOpenBrace = 1 << 4,
        JSDoc = 1 << 5,
    }

    /// <summary>parser.ts: `const enum SpeculationKind`.</summary>
    internal enum SpeculationKind
    {
        TryParse,
        Lookahead,
        Reparse,
    }

    /// <summary>parser.ts: `const enum ParsingContext` (the values are bit positions: `parsingContext |= 1 &lt;&lt; kind`).</summary>
    internal enum ParsingContext
    {
        SourceElements, // Elements in source file
        BlockStatements, // Statements in block
        SwitchClauses, // Clauses in switch statement
        SwitchClauseStatements, // Statements in switch clause
        TypeMembers, // Members in interface or type literal
        ClassMembers, // Members in class declaration
        EnumMembers, // Members in enum declaration
        HeritageClauseElement, // Elements in a heritage clause
        VariableDeclarations, // Variable declarations in variable statement
        ObjectBindingElements, // Binding elements in object binding list
        ArrayBindingElements, // Binding elements in array binding list
        ArgumentExpressions, // Expressions in argument list
        ObjectLiteralMembers, // Members in object literal
        JsxAttributes, // Attributes in jsx element
        JsxChildren, // Things between opening and closing JSX tags
        ArrayLiteralMembers, // Members in array literal
        Parameters, // Parameters in parameter list
        JSDocParameters, // JSDoc parameters in parameter list of JSDoc function type
        RestProperties, // Property names in a rest type list
        TypeParameters, // Type parameters in type parameter list
        TypeArguments, // Type arguments in type argument list
        TupleElementTypes, // Element types in tuple element type list
        HeritageClauses, // Heritage clauses for a class or interface declaration.
        ImportOrExportSpecifiers, // Named import clause's import specifier list,
        ImportAttributes, // Import attributes
        JSDocComment, // Parsing via JSDocParser
        Count, // Number of parsing contexts
    }

    /// <summary>parser.ts: `const enum Tristate`.</summary>
    internal enum Tristate
    {
        False,
        True,
        Unknown,
    }
}
