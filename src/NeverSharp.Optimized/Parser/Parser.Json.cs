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

namespace NeverSharp.Optimized;

/// <summary>parser.ts: Parser.parseJsonText.</summary>
internal sealed partial class Parser
{
    internal SourceFile ParseJsonText(string fileName, string sourceText, ScriptTarget languageVersion = ScriptTarget.ES2015, SyntaxCursor? syntaxCursor = null, bool setParentNodes = false)
    {
        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, ScriptKind.JSON, JSDocParsingMode.ParseAll);
        sourceFlags = contextFlags;

        // Prime the scanner.
        NextToken();
        var pos = GetNodePos();
        NodeArray statements;
        EndOfFileToken endOfFileToken;
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            statements = CreateNodeArray(new NodeArray(), pos, pos);
            endOfFileToken = ParseTokenNode<EndOfFileToken>();
        }
        else
        {
            // Loop and synthesize an ArrayLiteralExpression if there are more than
            // one top-level expressions to ensure all input text is consumed.
            // (TypeScript: `let expressions: Expression[] | Expression | undefined`; the array is `expressionList`.)
            NodeArray? expressionList = null;
            Node? singleExpression = null;
            while (Token() != SyntaxKind.EndOfFileToken)
            {
                Node expression;
                switch (Token())
                {
                    case SyntaxKind.OpenBracketToken:
                        expression = ParseArrayLiteralExpression();
                        break;
                    case SyntaxKind.TrueKeyword:
                    case SyntaxKind.FalseKeyword:
                    case SyntaxKind.NullKeyword:
                        expression = ParseTokenNode();
                        break;
                    case SyntaxKind.MinusToken:
                        if (LookAhead(static p => p.NextToken() == SyntaxKind.NumericLiteral && p.NextToken() != SyntaxKind.ColonToken))
                        {
                            expression = ParsePrefixUnaryExpression();
                        }
                        else
                        {
                            expression = ParseObjectLiteralExpression();
                        }
                        break;
                    case SyntaxKind.NumericLiteral:
                    case SyntaxKind.StringLiteral:
                        if (LookAhead(static p => p.NextToken() != SyntaxKind.ColonToken))
                        {
                            expression = ParseLiteralNode();
                            break;
                        }
                        // falls through
                        expression = ParseObjectLiteralExpression();
                        break;
                    default:
                        expression = ParseObjectLiteralExpression();
                        break;
                }

                // Error recovery: collect multiple top-level expressions
                if (expressionList != null)
                {
                    expressionList.Add(expression);
                }
                else if (singleExpression != null)
                {
                    expressionList = [singleExpression, expression];
                }
                else
                {
                    singleExpression = expression;
                    if (Token() != SyntaxKind.EndOfFileToken)
                    {
                        ParseErrorAtCurrentToken(Diagnostics.Unexpected_token);
                    }
                }
            }

            var expressionNode = expressionList != null ? FinishNode(factory.CreateArrayLiteralExpression(expressionList), pos) : Debug.CheckDefined(singleExpression);
            var statement = factory.CreateExpressionStatement(expressionNode);
            FinishNode(statement, pos);
            statements = CreateNodeArray([statement], pos);
            endOfFileToken = ParseExpectedToken<EndOfFileToken>(SyntaxKind.EndOfFileToken, Diagnostics.Unexpected_token);
        }

        // Set source file so that errors will be reported with this file name
        var sourceFile = CreateSourceFile(fileName, ScriptTarget.ES2015, ScriptKind.JSON, /*isDeclarationFile*/ false, statements, endOfFileToken, sourceFlags, _ => { });

        if (setParentNodes)
        {
            FixupParentReferences(sourceFile);
        }

        sourceFile.NodeCount = nodeCount;
        sourceFile.IdentifierCount = identifierCount;
        sourceFile.Identifiers = identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(parseDiagnostics, sourceFile);
        if (jsDocDiagnostics != null)
        {
            sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(jsDocDiagnostics, sourceFile);
        }

        var result = sourceFile;
        ClearState();
        return result;
    }
}
