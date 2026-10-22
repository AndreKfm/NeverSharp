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
    internal Node ParseJsonText(NodeStore nodeStore, string fileName, char[] sourceText, ScriptTarget languageVersion = ScriptTarget.ES2015, SyntaxCursor? syntaxCursor = null, bool setParentNodes = false)
    {
        UseStore(nodeStore);
        InitializeState(fileName, sourceText, languageVersion, syntaxCursor, ScriptKind.JSON, JSDocParsingMode.ParseAll);
        sourceFlags = contextFlags;

        // Prime the scanner.
        NextToken();
        var pos = GetNodePos();
        NodeList statements;
        Node endOfFileToken;
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            statements = CreateNodeArray([], pos, pos);
            endOfFileToken = ParseTokenNode();
        }
        else
        {
            // Loop and synthesize an ArrayLiteralExpression if there are more than
            // one top-level expressions to ensure all input text is consumed.
            // (TypeScript: `let expressions: Expression[] | Expression | undefined`; the array is `expressionList`, a list
            // under construction once hasExpressionList is set.)
            ListBuilder expressionList = default;
            var hasExpressionList = false;
            Node singleExpression = default;
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
                        if (LookAhead(new F.NextTokenIsNumericLiteralNotFollowedByColon()))
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
                        if (LookAhead(new F.NextTokenIsNotColon()))
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
                if (hasExpressionList)
                {
                    store.Push(expressionList, expression);
                }
                else if (!singleExpression.IsNone)
                {
                    expressionList = store.BeginList();
                    hasExpressionList = true;
                    store.Push(expressionList, singleExpression);
                    store.Push(expressionList, expression);
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

            // (The plain array becomes a NodeArray with range -1 as createArrayLiteralExpression's createNodeArray makes it.)
            var expressionNode = hasExpressionList ? FinishNode(factory.CreateArrayLiteralExpression(NodeFactory.CreateNodeArray(store, expressionList)), pos) : Debug.CheckDefined(singleExpression);
            var statement = factory.CreateExpressionStatement(expressionNode);
            FinishNode(statement, pos);
            statements = CreateNodeArray([statement], pos);
            endOfFileToken = ParseExpectedToken(SyntaxKind.EndOfFileToken, Diagnostics.Unexpected_token);
        }

        // Set source file so that errors will be reported with this file name
        var sourceFile = CreateSourceFile(fileName, ScriptTarget.ES2015, ScriptKind.JSON, /*isDeclarationFile*/ false, statements, endOfFileToken, sourceFlags, _ => { });

        if (setParentNodes)
        {
            FixupParentReferences(store, sourceFile);
        }

        store.SetNodeCount(sourceFile, nodeCount);
        store.SetIdentifierCount(sourceFile, identifierCount);
        store.SetIdentifiers(sourceFile, texts);
        store.SetParseDiagnostics(sourceFile, Utilities.AttachFileToDiagnostics(store, parseDiagnostics, sourceFile));
        if (jsDocDiagnostics != null)
        {
            store.SetJsDocDiagnostics(sourceFile, Utilities.AttachFileToDiagnostics(store, jsDocDiagnostics, sourceFile));
        }

        var result = sourceFile;
        ClearState();
        return result;
    }
}
